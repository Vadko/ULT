using Newtonsoft.Json.Linq;
using System.Windows;

namespace ULT;

public static class LanguageManagerService
{
    #region Check format
    public static bool IsUILocalization(JObject json) =>
        json.TryGetValue("m_Name", out var nameToken) && nameToken.Type == JTokenType.String
        && json.Property("m_languageKeys") != null
        && json.Property("m_languageValues") != null
        && json.Property("m_fieldValues") != null
        && json.Property("m_nextLanguageID") != null;

    public static bool IsTextDatabase(JObject json) =>
        json.TryGetValue("m_Name", out var nameToken) && nameToken.Type == JTokenType.String
        && json["Items"] is JArray
        && json["Languages"] is JArray;

    public static bool IsNamedDataArray(JObject json) =>
    json["dataArray"]?["Array"] is JArray arr && arr.Count > 0 && arr[0] is JObject first && first["name"] != null && first["key"] == null;

    public static bool IsSpeechManager(JObject json) =>
        json.Property("lines") != null && json["lines"]?["Array"] is JArray;

    public static bool IsLocalizationData(JObject json) =>
        json["KeyList"]?["Array"] is JArray &&
        json["SheetDataList"]?["Array"] is JArray;

    private static readonly HashSet<string> GameStringsNonLangFields = new HashSet<string> { "key", "category", "subcategory", "comments" };

    public static bool IsGameStringsArray(JObject json) =>
        json["dataArray"]?["Array"] is JArray arr && arr.Count > 0 &&
        arr[0] is JObject first && first["key"] != null && first["comments"] != null;
    #endregion

    #region Get languages
    public static List<string> GetLanguages(JObject json)
    {
        if (IsUILocalization(json))
        {
            var keysArray = json["m_languageKeys"]?["Array"] as JArray;
            return keysArray != null
                ? keysArray.Select(l => l.ToString()).ToList()
                : new List<string>();
        }

        if (IsTextDatabase(json))
        {
            var langs = json["Languages"] as JArray;
            return langs != null
                ? langs.Select(l => l.ToString()).ToList()
                : new List<string>();
        }

        if (IsSpeechManager(json))
        {
            var langsArray = json["languages"]?["Array"] as JArray;
            return langsArray != null
                ? langsArray.Select(l => l.ToString()).ToList()
                : new List<string>();
        }

        if (IsNamedDataArray(json))
        {
            var arr = json["dataArray"]?["Array"] as JArray;
            if (arr == null || arr.Count == 0) return new List<string>();
            var first = arr[0] as JObject;
            return first?.Properties()
                .Select(p => p.Name)
                .Where(n => n != "name")
                .ToList() ?? new List<string>();
        }

        if (IsLocalizationData(json))
        {
            var sheet = (json["SheetDataList"]?["Array"] as JArray)
                ?.FirstOrDefault(s => (s["DataList"]?["Array"] as JArray)?.Count > 1);
            var dl = sheet?["DataList"]?["Array"] as JArray;
            if (dl == null)
                return new List<string>();

            return Enumerable.Range(1, dl.Count - 1)
                .Select(i => $"Language {i}")
                .ToList();
        }

        if (IsGameStringsArray(json))
        {
            var arr = json["dataArray"]?["Array"] as JArray;
            var first = arr?[0] as JObject;
            return first?.Properties()
                .Select(p => p.Name)
                .Where(n => !GameStringsNonLangFields.Contains(n))
                .ToList() ?? new List<string>();
        }

        var oldLangs = GetJsonArray(json, "mSource.mLanguages.Array", "mLanguages.Array");
        if (oldLangs != null)
        {
            return oldLangs.OfType<JObject>()
                           .Where(l => l.TryGetValue("Name", out _))
                           .Select(l => l["Name"].ToString())
                           .ToList();
        }
        return new List<string>();
    }

    public static (bool name, bool code, bool flags) GetInputFieldConfig(JObject json)
    {
        if (IsUILocalization(json)) return (false, true, false);
        if (IsTextDatabase(json)) return (true, false, false);
        if (IsSpeechManager(json)) return (true, false, false);
        if (IsNamedDataArray(json)) return (false, true, false); 
        if (IsLocalizationData(json)) return (false, false, false); 
        if (IsGameStringsArray(json)) return (false, true, false); 
        return (true, true, true);
    }
    #endregion

    #region Remove languages
    public static bool RemoveLanguages(JObject json, List<int> indicesToKeep)
    {
        if (indicesToKeep == null || indicesToKeep.Count == 0)
        {
            MessageBox.Show("Потрібно залишити хоча б одну мову.", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var allLangs = GetLanguages(json);
        if (indicesToKeep.Count >= allLangs.Count)
        {
            MessageBox.Show("Не можна видаляти всі мови.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        try
        {
            if (IsUILocalization(json)) return RemoveUILocalizationLanguages(json, indicesToKeep);
            if (IsTextDatabase(json)) return RemoveTextDatabaseLanguages(json, indicesToKeep);
            if (IsSpeechManager(json)) return RemoveSpeechManagerLanguages(json, indicesToKeep);
            if (IsNamedDataArray(json)) return RemoveNamedDataLanguages(json, indicesToKeep);
            if (IsLocalizationData(json)) return RemoveLocalizationDataLanguages(json, indicesToKeep);
            if (IsGameStringsArray(json)) return RemoveGameStringsLanguages(json, indicesToKeep);
            return RemoveOldFormatLanguages(json, indicesToKeep);
        }
        catch
        {
            MessageBox.Show("Сталася помилка при видаленні мов.", "Отакої", MessageBoxButton.OK);
            return false;
        }
    }

    private static bool RemoveTextDatabaseLanguages(JObject json, List<int> selectedIndices)
    {
        var langs = json["Languages"] as JArray;
        if (langs == null) return false;

        RemoveUnselectedItems(langs, selectedIndices);

        var items = json["Items"] as JArray;
        if (items != null)
        {
            foreach (var item in items.OfType<JObject>())
            {
                var texts = item["Texts"] as JArray;
                if (texts != null)
                    item["Texts"] = new JArray(selectedIndices.Select(idx => idx < texts.Count ? texts[idx] : ""));
            }
        }
        return true;
    }

    private static bool RemoveSpeechManagerLanguages(JObject json, List<int> selectedIndices)
    {
        var langsArray = json["languages"]?["Array"] as JArray;
        if (langsArray == null) return false;

        if (!selectedIndices.Contains(0))
        {
            MessageBox.Show("Першу мову не можна видаляти у даному форматі.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        RemoveUnselectedItems(langsArray, selectedIndices);

        var lines = json["lines"]?["Array"] as JArray;
        if (lines != null)
        {
            foreach (var line in lines.OfType<JObject>())
            {
                var translation = line["translationText"]?["Array"] as JArray;
                if (translation != null)
                {
                    var translationIndices = selectedIndices.Where(i => i > 0).Select(i => i - 1).ToList();
                    RemoveUnselectedItems(translation, translationIndices);
                }
            }
        }

        foreach (var name in new[] { "languageIsRightToLeft", "languageAudioAssetBundles", "languageLipsyncAssetBundles" })
        {
            var arr = json[name]?["Array"] as JArray;
            if (arr != null) RemoveUnselectedItems(arr, selectedIndices);
        }
        return true;
    }

    private static bool RemoveUILocalizationLanguages(JObject json, List<int> selectedIndices)
    {
        var keysArray = json["m_languageKeys"]?["Array"] as JArray;
        var valuesArray = json["m_languageValues"]?["Array"] as JArray;

        if (keysArray == null || valuesArray == null) return false;
        if (keysArray.Count != valuesArray.Count) return false;

        var remainingLanguageIDs = new HashSet<int>();
        foreach (int idx in selectedIndices)
            if (idx < valuesArray.Count)
                remainingLanguageIDs.Add(valuesArray[idx].Value<int>());

        RemoveUnselectedItems(keysArray, selectedIndices);
        RemoveUnselectedItems(valuesArray, selectedIndices);

        var fieldValues = json["m_fieldValues"]?["Array"] as JArray;
        if (fieldValues != null)
        {
            foreach (var field in fieldValues.OfType<JObject>())
            {
                var fieldKeys = field["m_keys"]?["Array"] as JArray;
                var fieldVals = field["m_values"]?["Array"] as JArray;

                if (fieldKeys != null && fieldVals != null)
                {
                    var indicesToKeep = new List<int>();
                    for (int i = 0; i < fieldKeys.Count; i++)
                        if (remainingLanguageIDs.Contains(fieldKeys[i].Value<int>()))
                            indicesToKeep.Add(i);

                    RemoveUnselectedItems(fieldKeys, indicesToKeep);
                    RemoveUnselectedItems(fieldVals, indicesToKeep);
                }
            }
        }
        return true;
    }

    private static bool RemoveOldFormatLanguages(JObject json, List<int> selectedIndices)
    {
        var langsArray = GetJsonArray(json, "mSource.mLanguages.Array", "mLanguages.Array");
        if (langsArray == null) return false;

        RemoveUnselectedItems(langsArray, selectedIndices);

        var termsArray = GetJsonArray(json, "mSource.mTerms.Array", "mTerms.Array");
        if (termsArray != null)
        {
            foreach (var term in termsArray.OfType<JObject>())
            {
                var langArr = term["Languages"]?["Array"] as JArray;
                var flagsArr = term["Flags"]?["Array"] as JArray;
                if (langArr != null) RemoveUnselectedItems(langArr, selectedIndices);
                if (flagsArr != null) RemoveUnselectedItems(flagsArr, selectedIndices);
            }
        }
        return true;
    }

    private static bool RemoveNamedDataLanguages(JObject json, List<int> indicesToKeep)
    {
        var arr = json["dataArray"]?["Array"] as JArray;
        if (arr == null || arr.Count == 0) return false;

        var first = arr[0] as JObject;
        if (first == null) return false;

        var langKeys = first.Properties()
            .Select(p => p.Name)
            .Where(n => n != "name")
            .ToList();

        var keepSet = new HashSet<int>(indicesToKeep);
        var keysToRemove = langKeys
            .Select((key, idx) => new { key, idx })
            .Where(x => !keepSet.Contains(x.idx))
            .Select(x => x.key)
            .ToList();

        foreach (var item in arr.OfType<JObject>())
            foreach (var key in keysToRemove)
                item.Remove(key);

        return true;
    }

    private static bool RemoveLocalizationDataLanguages(JObject json, List<int> indicesToKeep)
    {
        var sheetList = json["SheetDataList"]?["Array"] as JArray;
        if (sheetList == null) return false;

        var firstDl = sheetList
            .OfType<JObject>()
            .Select(s => s["DataList"]?["Array"] as JArray)
            .FirstOrDefault(dl => dl?.Count > 1);

        if (firstDl == null) return false;

        int langCount = firstDl.Count - 1;
        var keepSet = new HashSet<int>(indicesToKeep);

        foreach (var sheet in sheetList.OfType<JObject>())
        {
            var dl = sheet["DataList"]?["Array"] as JArray;
            if (dl == null) continue;

            var newDl = new JArray { dl[0] };
            for (int i = 0; i < langCount; i++)
            {
                if (keepSet.Contains(i) && i + 1 < dl.Count)
                    newDl.Add(dl[i + 1]);
            }
            sheet["DataList"]["Array"] = newDl;
        }

        var fontList = json["FontDataList"]?["Array"] as JArray;
        if (fontList != null)
        {
            var fontsToKeep = new JArray();
            var fontsSorted = fontList.OfType<JObject>()
                .OrderBy(f => f["Language"]?.ToObject<int>() ?? 0)
                .ToList();

            foreach (var idx in indicesToKeep.OrderBy(x => x))
            {
                if (idx < fontsSorted.Count)
                    fontsToKeep.Add(fontsSorted[idx]);
            }
            json["FontDataList"]["Array"] = fontsToKeep;
        }

        return true;
    }

    private static bool RemoveGameStringsLanguages(JObject json, List<int> indicesToKeep)
    {
        var arr = json["dataArray"]?["Array"] as JArray;
        if (arr == null || arr.Count == 0) return false;

        var first = arr[0] as JObject;
        if (first == null) return false;

        var langKeys = first.Properties()
            .Select(p => p.Name)
            .Where(n => !GameStringsNonLangFields.Contains(n))
            .ToList();

        var keepSet = new HashSet<int>(indicesToKeep);
        var keysToRemove = langKeys
            .Select((key, idx) => new { key, idx })
            .Where(x => !keepSet.Contains(x.idx))
            .Select(x => x.key)
            .ToList();

        foreach (var item in arr.OfType<JObject>())
            foreach (var key in keysToRemove)
                item.Remove(key);

        return true;
    }
    #endregion

    #region Add languages
    public static bool AddLanguage(JObject json, string name, string code, string flagsText)
    {
        try
        {
            if (IsUILocalization(json)) return AddUILocalizationLanguage(json, code);
            if (IsTextDatabase(json)) return AddTextDatabaseLanguage(json, name);
            if (IsSpeechManager(json)) return AddSpeechManagerLanguage(json, name);
            if (IsNamedDataArray(json)) return AddNamedDataLanguage(json, code);
            if (IsLocalizationData(json)) return AddLocalizationDataLanguage(json);
            if (IsGameStringsArray(json)) return AddGameStringsLanguage(json, code);
            return AddOldFormatLanguage(json, name, code, flagsText);
        }
        catch
        {
            MessageBox.Show("Сталася помилка при додаванні мови.", "Отакої", MessageBoxButton.OK);
            return false;
        }
    }

    private static bool AddTextDatabaseLanguage(JObject json, string name)
    {
        if (!int.TryParse(name, out int langNumber))
        {
            MessageBox.Show("Для цієї структури Name має бути цілим числом.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        var langs = json["Languages"] as JArray;
        if (langs == null) return false;

        langs.Add(langNumber);

        var items = json["Items"] as JArray;
        if (items != null)
            foreach (var item in items.OfType<JObject>())
                (item["Texts"] as JArray)?.Add("");
        return true;
    }

    private static bool AddSpeechManagerLanguage(JObject json, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        var langs = json["languages"]?["Array"] as JArray;
        if (langs == null) return false;

        if (langs.Any(l => string.Equals(l.ToString(), name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Мова '{name}' вже існує.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        langs.Add(name);
        (json["languageIsRightToLeft"]?["Array"] as JArray)?.Add(0);
        (json["languageAudioAssetBundles"]?["Array"] as JArray)?.Add("");
        (json["languageLipsyncAssetBundles"]?["Array"] as JArray)?.Add("");

        var lines = json["lines"]?["Array"] as JArray;
        if (lines != null)
            foreach (var line in lines.OfType<JObject>())
                (line["translationText"]?["Array"] as JArray)?.Add("");
        return true;
    }

    private static bool AddUILocalizationLanguage(JObject json, string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("Потрібно вказати код мови.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        var keysArray = json["m_languageKeys"]?["Array"] as JArray;
        var valuesArray = json["m_languageValues"]?["Array"] as JArray;
        if (keysArray == null || valuesArray == null) return false;

        if (keysArray.Any(l => string.Equals(l.ToString(), code, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Мова '{code}' вже існує.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        int nextID = json.Value<int>("m_nextLanguageID");
        keysArray.Add(code);
        valuesArray.Add(nextID);
        json["m_nextLanguageID"] = nextID + 1;

        var fieldValues = json["m_fieldValues"]?["Array"] as JArray;
        if (fieldValues != null)
        {
            foreach (var field in fieldValues.OfType<JObject>())
            {
                (field["m_keys"]?["Array"] as JArray)?.Add(nextID);
                (field["m_values"]?["Array"] as JArray)?.Add("");
            }
        }
        return true;
    }

    private static bool AddOldFormatLanguage(JObject json, string name, string code, string flagsText)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(flagsText))
            return false;

        if (!int.TryParse(flagsText, out int flags))
        {
            MessageBox.Show("Поле Flags повинно бути числом.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        var langsArray = GetJsonArray(json, "mSource.mLanguages.Array", "mLanguages.Array");
        if (langsArray == null) return false;

        foreach (var lang in langsArray.OfType<JObject>())
        {
            if (lang.TryGetValue("Name", out var n) && string.Equals(n.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Мова з назвою '{name}' вже існує.", "Помилка", MessageBoxButton.OK);
                return false;
            }
            if (lang.TryGetValue("Code", out var c) && string.Equals(c.ToString(), code, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Мова з кодом '{code}' вже існує.", "Помилка", MessageBoxButton.OK);
                return false;
            }
        }

        langsArray.Add(new JObject { ["Name"] = name, ["Code"] = code, ["Flags"] = flags });

        var termsArray = GetJsonArray(json, "mSource.mTerms.Array", "mTerms.Array");
        if (termsArray != null)
        {
            foreach (var term in termsArray.OfType<JObject>())
            {
                AddOrUpdateArray(term, "Languages", "");
                AddOrUpdateArray(term, "Flags", 0);
            }
        }
        return true;
    }

    private static bool AddNamedDataLanguage(JObject json, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Вкажіть код нової мови (напр. ukrainian).", "Помилка", MessageBoxButton.OK);
            return false;
        }

        var arr = json["dataArray"]?["Array"] as JArray;
        if (arr == null) return false;

        if (arr[0] is JObject firstCheck && firstCheck[code] != null)
        {
            MessageBox.Show($"Мова '{code}' вже існує.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        foreach (var item in arr.OfType<JObject>())
            item[code] = "";

        return true;
    }

    private static bool AddLocalizationDataLanguage(JObject json)
    {
        var fontList = json["FontDataList"]?["Array"] as JArray;
        int newLangId = 1;
        if (fontList != null && fontList.Count > 0)
        {
            newLangId = fontList
                .OfType<JObject>()
                .Select(f => f["Language"]?.ToObject<int>() ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var lastFont = fontList.Last as JObject;
            var newFont = lastFont != null
                ? (JObject)lastFont.DeepClone()
                : new JObject { ["Font"] = new JObject { ["m_FileID"] = 0, ["m_PathID"] = 0 }, ["TMPFont"] = new JObject { ["m_FileID"] = 0, ["m_PathID"] = 0 } };
            newFont["Language"] = newLangId;
            fontList.Add(newFont);
        }

        var sheetList = json["SheetDataList"]?["Array"] as JArray;
        if (sheetList == null) return false;

        foreach (var sheet in sheetList.OfType<JObject>())
        {
            var dl = sheet["DataList"]?["Array"] as JArray;
            dl?.Add("");
        }

        return true;
    }

    private static bool AddGameStringsLanguage(JObject json, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Вкажіть код нової мови (напр. uk).", "Помилка", MessageBoxButton.OK);
            return false;
        }

        var arr = json["dataArray"]?["Array"] as JArray;
        if (arr == null) return false;

        if (arr[0] is JObject firstCheck && firstCheck[code] != null)
        {
            MessageBox.Show($"Мова '{code}' вже існує.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        foreach (var item in arr.OfType<JObject>())
            item[code] = "";

        return true;
    }
    #endregion

    #region Helpers
    private static void RemoveUnselectedItems(JArray array, List<int> selectedIndices)
    {
        var keepSet = new HashSet<int>(selectedIndices);
        for (int i = array.Count - 1; i >= 0; i--)
            if (!keepSet.Contains(i))
                array.RemoveAt(i);
    }

    private static void AddOrUpdateArray(JObject parent, string key, object value)
    {
        if (parent.TryGetValue(key, out var token) && token is JObject obj)
        {
            if (obj.TryGetValue("Array", out var arrToken) && arrToken is JArray arr)
                arr.Add(value);
            else
                obj["Array"] = new JArray { value };
        }
        else
        {
            parent[key] = new JObject { ["Array"] = new JArray { value } };
        }
    }

    public static JArray GetJsonArray(JObject json, params string[] paths)
    {
        foreach (var path in paths)
        {
            JToken current = json;
            foreach (var token in path.Split('.'))
            {
                if (current is JObject o && o.TryGetValue(token, out var next))
                    current = next;
                else { current = null; break; }
            }
            if (current is JArray array) return array;
        }
        return null;
    }
    #endregion
}