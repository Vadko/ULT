using Newtonsoft.Json.Linq;

namespace ULT;

public class ArrayFormatHandler : BaseFormatHandler
{
    private JsonFormatType formatType;
    private string[] _gameStringLangKeys;
    public JsonFormatType GetFormatType() => formatType;

    private string[] GetGameStringLanguageKeys()
    {
        if (_gameStringLangKeys != null)
            return _gameStringLangKeys;

        var dataArray = jsonData["dataArray"]?["Array"] as JArray;
        if (dataArray == null || dataArray.Count == 0)
            return _gameStringLangKeys = Array.Empty<string>();

        if (dataArray[0] is not JObject firstItem)
            return _gameStringLangKeys = Array.Empty<string>();

        _gameStringLangKeys = firstItem.Properties()
            .Select(p => p.Name)
            .Where(n => n != "key" && n != "category" && n != "subcategory" && n != "comments")
            .ToArray();

        return _gameStringLangKeys;
    }

    public void SetFormatType(JsonFormatType type)
    {
        formatType = type;
    }

    public override List<DataGridItem> LoadTerms()
    {
        if (formatType == JsonFormatType.TermsArray)
            return LoadTermsArray();

        if (formatType == JsonFormatType.ItemsArray)
            return LoadItemsArray();

        if (formatType == JsonFormatType.LinesArray)
            return LoadLinesArray();

        if (formatType == JsonFormatType.TableDataArray)
            return LoadTableDataArray();

        if (formatType == JsonFormatType.LanguageKeysArray)
            return LoadLanguageKeysArray();

        if (formatType == JsonFormatType.GameStringsArray)
            return LoadGameStringsArray();

        if (formatType == JsonFormatType.LocaKeysLocaValues)
            return LoadLocaKeysLocaValues();

        if (formatType == JsonFormatType.TextContentsArray)
            return LoadTextContentsArray();

        if (formatType == JsonFormatType.TinyTextAsset)
            return LoadTinyTextAsset();

        if (formatType == JsonFormatType.PassagesArray)
            return LoadPassagesArray();

        if (formatType == JsonFormatType.NamedDataArray)
            return LoadNamedDataArray();

        if (formatType == JsonFormatType.LocalizationData)
            return LoadLocalizationData();

        if (formatType == JsonFormatType.KeysValuesTable)
            return LoadKeysValuesTable();

        return new List<DataGridItem>();
    }

    public override void SaveTerms(List<DataGridItem> terms)
    {
        switch (formatType)
        {
            case JsonFormatType.TermsArray:
                SaveTermsArray(terms);
                break;
            case JsonFormatType.ItemsArray:
                SaveItemsArray(terms);
                break;
            case JsonFormatType.LinesArray:
                SaveLinesArray(terms);
                break;
            case JsonFormatType.TableDataArray:
                SaveTableDataArray(terms);
                break;
            case JsonFormatType.LanguageKeysArray:
                SaveLanguageKeysArray(terms);
                break;
            case JsonFormatType.GameStringsArray:
                SaveGameStringsArray(terms);
                break;
            case JsonFormatType.LocaKeysLocaValues:
                SaveLocaKeysLocaValues(terms);
                break;
            case JsonFormatType.TextContentsArray:
                SaveTextContentsArray(terms);
                break;
            case JsonFormatType.TinyTextAsset:
                SaveTinyTextAsset(terms);
                break;
            case JsonFormatType.PassagesArray:
                SavePassagesArray(terms);
                break;
            case JsonFormatType.NamedDataArray:
                SaveNamedDataArray(terms);
                break;
            case JsonFormatType.LocalizationData:
                SaveLocalizationData(terms);
                break;
            case JsonFormatType.KeysValuesTable:
                SaveKeysValuesTable(terms);
                break;
        }
    }

    #region TermsArray Format
    private List<DataGridItem> LoadTermsArray()
    {
        var terms = new List<DataGridItem>();
        var termsArray = GetTermsArray(jsonData) as JArray;

        if (termsArray == null) return terms;

        for (int i = 0; i < termsArray.Count; i++)
        {
            var item = termsArray[i];
            string term = (string)item["Term"];
            var langs = item["Languages"]?["Array"]?.ToList();

            if (langs != null && origIndex < langs.Count && transIndex < langs.Count)
            {
                var orig = langs[origIndex]?.ToString() ?? "";
                var trans = langs[transIndex]?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(orig))
                {
                    terms.Add(new DataGridItem
                    {
                        ID = term ?? "",
                        Text = ConvertNewlinesToMarkers(orig),
                        Translation = ConvertNewlinesToMarkers(trans),
                        JsonIndex = i
                    });
                }
            }
        }

        return terms;
    }

    private void SaveTermsArray(List<DataGridItem> terms)
    {
        var termsArray = GetTermsArray(jsonData) as JArray;
        if (termsArray == null) return;

        foreach (var entry in terms)
        {
            var item = termsArray[entry.JsonIndex];
            var langArray = item["Languages"]?["Array"] as JArray;

            if (langArray == null)
            {
                langArray = new JArray();
                item["Languages"] = new JObject { ["Array"] = langArray };
            }

            while (langArray.Count <= transIndex)
                langArray.Add("");

            langArray[transIndex] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region ItemsArray Format
    private List<DataGridItem> LoadItemsArray()
    {
        var terms = new List<DataGridItem>();
        var termsArray = jsonData["Items"] as JArray;

        if (termsArray == null) return terms;

        for (int i = 0; i < termsArray.Count; i++)
        {
            var item = termsArray[i];
            string term = (string)item["Id"];
            var langs = item["Texts"]?.ToList();

            if (langs != null && origIndex < langs.Count && transIndex < langs.Count)
            {
                var orig = langs[origIndex]?.ToString() ?? "";
                var trans = langs[transIndex]?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(orig))
                {
                    terms.Add(new DataGridItem
                    {
                        ID = term ?? "",
                        Text = ConvertNewlinesToMarkers(orig),
                        Translation = ConvertNewlinesToMarkers(trans),
                        JsonIndex = i
                    });
                }
            }
        }

        return terms;
    }

    private void SaveItemsArray(List<DataGridItem> terms)
    {
        var termsArray = jsonData["Items"] as JArray;
        if (termsArray == null) return;

        foreach (var entry in terms)
        {
            var item = termsArray[entry.JsonIndex];
            var texts = item["Texts"] as JArray;

            if (texts == null)
            {
                texts = new JArray();
                item["Texts"] = texts;
            }

            while (texts.Count <= transIndex)
                texts.Add("");

            texts[transIndex] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region LinesArray Format
    private List<DataGridItem> LoadLinesArray()
    {
        var terms = new List<DataGridItem>();
        var termsArray = jsonData["lines"]["Array"] as JArray;

        if (termsArray == null) return terms;

        for (int i = 0; i < termsArray.Count; i++)
        {
            var item = termsArray[i];
            string term = item["lineID"]?.ToString() ?? i.ToString();
            var transArr = item["translationText"]?["Array"] as JArray;

            if (transArr == null) continue;

            string orig = GetTranslationByIndex(item, transArr, origIndex);
            string trans = GetTranslationByIndex(item, transArr, transIndex);

            if (!string.IsNullOrWhiteSpace(orig))
            {
                terms.Add(new DataGridItem
                {
                    ID = term,
                    Text = ConvertNewlinesToMarkers(orig),
                    Translation = ConvertNewlinesToMarkers(trans),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private string GetTranslationByIndex(JToken item, JArray transArr, int index)
    {
        if (index == 0)
            return item["text"]?.ToString() ?? "";
        else if (index > 0 && transArr.Count >= index)
            return transArr[index - 1]?.ToString() ?? "";

        return "";
    }

    private void SaveLinesArray(List<DataGridItem> terms)
    {
        var termsArray = jsonData["lines"]["Array"] as JArray;
        if (termsArray == null) return;

        foreach (var entry in terms)
        {
            var item = termsArray[entry.JsonIndex];
            var transArr = item["translationText"]?["Array"] as JArray;

            if (transArr == null)
            {
                transArr = new JArray();
                item["translationText"] = new JObject { ["Array"] = transArr };
            }

            if (transIndex == 0)
            {
                item["text"] = ConvertMarkersToNewlines(entry.Translation);
            }
            else
            {
                while (transArr.Count < transIndex)
                    transArr.Add("");

                transArr[transIndex - 1] = ConvertMarkersToNewlines(entry.Translation);
            }
        }
    }
    #endregion

    #region TableDataArray Format
    private List<DataGridItem> LoadTableDataArray()
    {
        var terms = new List<DataGridItem>();
        var tableArray = jsonData["m_TableData"]["Array"] as JArray;

        if (tableArray == null) return terms;

        for (int i = 0; i < tableArray.Count; i++)
        {
            var item = tableArray[i];
            string id = item["m_Id"]?.ToString() ?? i.ToString();
            string text = item["m_Localized"]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(text))
            {
                terms.Add(new DataGridItem
                {
                    ID = id,
                    Text = ConvertNewlinesToMarkers(text),
                    Translation = ConvertNewlinesToMarkers(text),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private void SaveTableDataArray(List<DataGridItem> terms)
    {
        var tableArray = jsonData["m_TableData"]["Array"] as JArray;
        if (tableArray == null) return;

        foreach (var entry in terms)
        {
            tableArray[entry.JsonIndex]["m_Localized"] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region LanguageKeysArray Format
    private List<DataGridItem> LoadLanguageKeysArray()
    {
        var terms = new List<DataGridItem>();
        var languageValues = jsonData["m_languageValues"]["Array"] as JArray;
        var fieldValues = jsonData["m_fieldValues"]["Array"] as JArray;

        if (languageValues == null || fieldValues == null) return terms;
        if (origIndex < 0 || origIndex >= languageValues.Count ||
            transIndex < 0 || transIndex >= languageValues.Count)
            return terms;

        int origLangValue = languageValues[origIndex].ToObject<int>();
        int transLangValue = languageValues[transIndex].ToObject<int>();

        for (int fieldIdx = 0; fieldIdx < fieldValues.Count; fieldIdx++)
        {
            var field = fieldValues[fieldIdx];
            string fieldName = field["m_fieldName"]?.ToString();
            if (string.IsNullOrEmpty(fieldName)) continue;

            var keys = field["m_keys"]?["Array"] as JArray;
            var values = field["m_values"]?["Array"] as JArray;
            if (keys == null || values == null) continue;

            var keyIndexMap = BuildKeyIndexMap(keys);

            if (!keyIndexMap.TryGetValue(origLangValue, out int origKeyIndex)) continue;

            keyIndexMap.TryGetValue(transLangValue, out int transKeyIndex);

            string originalText = origKeyIndex < values.Count
                ? values[origKeyIndex]?.ToString() ?? "" : "";
            string translationText = keyIndexMap.ContainsKey(transLangValue) && transKeyIndex < values.Count
                ? values[transKeyIndex]?.ToString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(originalText)) continue;

            terms.Add(new DataGridItem
            {
                ID = fieldName,
                Text = ConvertNewlinesToMarkers(originalText),
                Translation = ConvertNewlinesToMarkers(
                    string.IsNullOrEmpty(translationText) ? originalText : translationText),
                JsonIndex = fieldIdx
            });
        }

        return terms;
    }

    private void SaveLanguageKeysArray(List<DataGridItem> terms)
    {
        var languageValues = jsonData["m_languageValues"]["Array"] as JArray;
        var fieldValues = jsonData["m_fieldValues"]["Array"] as JArray;

        if (languageValues == null || fieldValues == null) return;
        if (transIndex < 0 || transIndex >= languageValues.Count) return;

        int targetLangValue = languageValues[transIndex].ToObject<int>();

        foreach (var entry in terms)
        {
            if (entry.JsonIndex < 0 || entry.JsonIndex >= fieldValues.Count) continue;

            var field = fieldValues[entry.JsonIndex];
            var keys = field["m_keys"]?["Array"] as JArray;
            var values = field["m_values"]?["Array"] as JArray;
            if (keys == null || values == null) continue;

            string translatedValue = ConvertMarkersToNewlines(entry.Translation);

            var keyIndexMap = BuildKeyIndexMap(keys);

            if (keyIndexMap.TryGetValue(targetLangValue, out int keyIndex) && keyIndex < values.Count)
            {
                values[keyIndex] = translatedValue;
            }
            else
            {
                keys.Add(targetLangValue);
                values.Add(translatedValue);
            }
        }
    }

    private static Dictionary<int, int> BuildKeyIndexMap(JArray keys)
    {
        var map = new Dictionary<int, int>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
            map[keys[i].ToObject<int>()] = i;
        return map;
    }
    #endregion

    #region GameStringsArray Format
    private List<DataGridItem> LoadGameStringsArray()
    {
        var terms = new List<DataGridItem>();
        var dataArray = jsonData["dataArray"]?["Array"] as JArray;
        if (dataArray == null) return terms;

        var langKeys = GetGameStringLanguageKeys();
        if (langKeys.Length == 0 || origIndex >= langKeys.Length || transIndex >= langKeys.Length)
            return terms;

        string origLang = langKeys[origIndex];
        string transLang = langKeys[transIndex];

        for (int i = 0; i < dataArray.Count; i++)
        {
            if (dataArray[i] is not JObject item) continue;

            string key = item["key"]?.ToString() ?? "";
            string orig = item[origLang]?.ToString() ?? "";
            string trans = item[transLang]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(orig))
            {
                terms.Add(new DataGridItem
                {
                    ID = key,
                    Text = ConvertNewlinesToMarkers(orig),
                    Translation = ConvertNewlinesToMarkers(trans),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private void SaveGameStringsArray(List<DataGridItem> terms)
    {
        var dataArray = jsonData["dataArray"]?["Array"] as JArray;
        if (dataArray == null) return;

        var langKeys = GetGameStringLanguageKeys();
        if (langKeys.Length == 0 || transIndex >= langKeys.Length) return;

        string transLang = langKeys[transIndex];

        foreach (var entry in terms)
        {
            if (entry.JsonIndex < 0 || entry.JsonIndex >= dataArray.Count) continue;
            if (dataArray[entry.JsonIndex] is JObject item)
                item[transLang] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region LocaKeysLocaValues Format
    private List<DataGridItem> LoadLocaKeysLocaValues()
    {
        var terms = new List<DataGridItem>();
        var keysArray = jsonData["mLocaKeys"]?["Array"] as JArray;
        var valuesArray = jsonData["mLocaValues"]?["Array"] as JArray;

        if (keysArray == null || valuesArray == null)
            return terms;

        int count = Math.Min(keysArray.Count, valuesArray.Count);

        for (int i = 0; i < count; i++)
        {
            string key = keysArray[i]?.ToString() ?? "";
            string value = valuesArray[i]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                terms.Add(new DataGridItem
                {
                    ID = key,
                    Text = ConvertNewlinesToMarkers(value),
                    Translation = ConvertNewlinesToMarkers(value),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private void SaveLocaKeysLocaValues(List<DataGridItem> terms)
    {
        var valuesArray = jsonData["mLocaValues"]?["Array"] as JArray;
        if (valuesArray == null) return;

        foreach (var entry in terms)
        {
            if (entry.JsonIndex >= 0 && entry.JsonIndex < valuesArray.Count)
            {
                valuesArray[entry.JsonIndex] = ConvertMarkersToNewlines(entry.Translation);
            }
        }
    }
    #endregion

    #region TextContentsArray Format
    private List<DataGridItem> LoadTextContentsArray()
    {
        var terms = new List<DataGridItem>();
        var contentsArray = (jsonData["textContents"]?["Array"] ?? jsonData["configList"]?["Array"]) as JArray;

        if (contentsArray == null) return terms;

        for (int i = 0; i < contentsArray.Count; i++)
        {
            var item = contentsArray[i];
            string id = item["id"]?.ToString() ?? "";
            string text = item["text"]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(text))
            {
                terms.Add(new DataGridItem
                {
                    ID = id,
                    Text = ConvertNewlinesToMarkers(text),
                    Translation = ConvertNewlinesToMarkers(text),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private void SaveTextContentsArray(List<DataGridItem> terms)
    {
        var contentsArray = (jsonData["textContents"]?["Array"] ?? jsonData["configList"]?["Array"]) as JArray;
        if (contentsArray == null) return;

        foreach (var entry in terms)
        {
            if (entry.JsonIndex >= 0 && entry.JsonIndex < contentsArray.Count)
            {
                contentsArray[entry.JsonIndex]["text"] = ConvertMarkersToNewlines(entry.Translation);
            }
        }
    }
    #endregion

    #region TinyTextAsset Format
    private List<DataGridItem> LoadTinyTextAsset()
    {
        var termEntries = new List<DataGridItem>();

        string text = (jsonData["m_Text"] ?? jsonData["m_text"])?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return termEntries;

        int pathId = jsonData["m_GameObject"]?["m_PathID"]?.ToObject<int>() ?? 0;
        string term = pathId.ToString();

        termEntries.Add(new DataGridItem
        {
            ID = term,
            Text = ConvertNewlinesToMarkers(text),
            Translation = ConvertNewlinesToMarkers(text),
            JsonIndex = 0
        });

        return termEntries;
    }

    private void SaveTinyTextAsset(List<DataGridItem> terms)
    {
        if (terms.Count == 0) return;

        string translatedValue = ConvertMarkersToNewlines(terms[0].Translation);

        if (jsonData["m_text"] != null)
            jsonData["m_text"] = translatedValue;
        else
            jsonData["m_Text"] = translatedValue;
    }
    #endregion

    #region PassagesArray Format
    private List<DataGridItem> LoadPassagesArray()
    {
        var terms = new List<DataGridItem>();
        var passagesArray = jsonData["passages"]?["Array"] as JArray;
        if (passagesArray == null) return terms;

        for (int i = 0; i < passagesArray.Count; i++)
        {
            var item = passagesArray[i];
            string text = item["text"]?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(text))
                continue;

            string id = item["id"]?.ToString() ?? i.ToString();
            string name = item["name"]?.ToString() ?? "";
            string term = string.IsNullOrWhiteSpace(name) ? id : $"{id}:{name}";

            terms.Add(new DataGridItem
            {
                ID = term,
                Text = ConvertNewlinesToMarkers(text),
                Translation = ConvertNewlinesToMarkers(text),
                JsonIndex = i
            });
        }

        return terms;
    }

    private void SavePassagesArray(List<DataGridItem> terms)
    {
        var passagesArray = jsonData["passages"]?["Array"] as JArray;
        if (passagesArray == null) return;

        foreach (var entry in terms)
        {
            if (entry.JsonIndex >= 0 && entry.JsonIndex < passagesArray.Count)
                passagesArray[entry.JsonIndex]["text"] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region NamedDataArray Format
    private string[] _namedDataLangKeys;

    private string[] GetNamedDataLanguageKeys()
    {
        if (_namedDataLangKeys != null)
            return _namedDataLangKeys;

        var dataArray = jsonData["dataArray"]?["Array"] as JArray;
        if (dataArray == null || dataArray.Count == 0)
            return _namedDataLangKeys = Array.Empty<string>();

        if (dataArray[0] is not JObject firstItem)
            return _namedDataLangKeys = Array.Empty<string>();

        _namedDataLangKeys = firstItem.Properties()
            .Select(p => p.Name)
            .Where(n => n != "name")
            .ToArray();

        return _namedDataLangKeys;
    }

    private List<DataGridItem> LoadNamedDataArray()
    {
        var terms = new List<DataGridItem>();
        var dataArray = jsonData["dataArray"]?["Array"] as JArray;
        if (dataArray == null) return terms;

        var langKeys = GetNamedDataLanguageKeys();
        if (langKeys.Length == 0 || origIndex >= langKeys.Length || transIndex >= langKeys.Length)
            return terms;

        string origLang = langKeys[origIndex];
        string transLang = langKeys[transIndex];

        for (int i = 0; i < dataArray.Count; i++)
        {
            if (dataArray[i] is not JObject item) continue;

            string name = item["name"]?.ToString() ?? "";
            string orig = item[origLang]?.ToString() ?? "";
            string trans = item[transLang]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(orig))
            {
                terms.Add(new DataGridItem
                {
                    ID = name,
                    Text = ConvertNewlinesToMarkers(orig),
                    Translation = ConvertNewlinesToMarkers(trans),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private void SaveNamedDataArray(List<DataGridItem> terms)
    {
        var dataArray = jsonData["dataArray"]?["Array"] as JArray;
        if (dataArray == null) return;

        var langKeys = GetNamedDataLanguageKeys();
        if (langKeys.Length == 0 || transIndex >= langKeys.Length) return;

        string transLang = langKeys[transIndex];

        foreach (var entry in terms)
        {
            if (entry.JsonIndex < 0 || entry.JsonIndex >= dataArray.Count) continue;
            if (dataArray[entry.JsonIndex] is JObject item)
                item[transLang] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region LocalizationData Format
    private List<DataGridItem> LoadLocalizationData()
    {
        var terms = new List<DataGridItem>();
        var sheetList = jsonData["SheetDataList"]?["Array"] as JArray;
        if (sheetList == null) return terms;

        for (int i = 0; i < sheetList.Count; i++)
        {
            var dl = sheetList[i]?["DataList"]?["Array"] as JArray;
            if (dl == null || dl.Count < 2) continue;

            string key = dl[0]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(key)) continue;

            int origSlot = origIndex + 1;
            int transSlot = transIndex + 1;

            if (origSlot >= dl.Count) continue;

            string orig = origSlot < dl.Count ? dl[origSlot]?.ToString() ?? "" : "";
            string trans = transSlot < dl.Count ? dl[transSlot]?.ToString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(orig)) continue;

            terms.Add(new DataGridItem
            {
                ID = key,
                Text = ConvertNewlinesToMarkers(orig),
                Translation = ConvertNewlinesToMarkers(trans),
                JsonIndex = i
            });
        }

        return terms;
    }

    private void SaveLocalizationData(List<DataGridItem> terms)
    {
        var sheetList = jsonData["SheetDataList"]?["Array"] as JArray;
        if (sheetList == null) return;

        int transSlot = transIndex + 1;

        foreach (var entry in terms)
        {
            if (entry.JsonIndex < 0 || entry.JsonIndex >= sheetList.Count) continue;

            var dl = sheetList[entry.JsonIndex]?["DataList"]?["Array"] as JArray;
            if (dl == null) continue;

            while (dl.Count <= transSlot)
                dl.Add("");

            dl[transSlot] = ConvertMarkersToNewlines(entry.Translation);
        }
    }
    #endregion

    #region KeysValuesTable Format
    private List<DataGridItem> LoadKeysValuesTable()
    {
        var terms = new List<DataGridItem>();
        var keysArray = jsonData["m_data"]?["m_keys"]?["Array"] as JArray;
        var valuesArray = jsonData["m_data"]?["m_values"]?["Array"] as JArray;

        if (keysArray == null || valuesArray == null) return terms;

        int count = Math.Min(keysArray.Count, valuesArray.Count);

        for (int i = 0; i < count; i++)
        {
            string key = keysArray[i]?.ToString() ?? "";
            string value = valuesArray[i]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                terms.Add(new DataGridItem
                {
                    ID = key,
                    Text = ConvertNewlinesToMarkers(value),
                    Translation = ConvertNewlinesToMarkers(value),
                    JsonIndex = i
                });
            }
        }

        return terms;
    }

    private void SaveKeysValuesTable(List<DataGridItem> terms)
    {
        var valuesArray = jsonData["m_data"]?["m_values"]?["Array"] as JArray;
        if (valuesArray == null) return;

        foreach (var entry in terms)
        {
            if (entry.JsonIndex >= 0 && entry.JsonIndex < valuesArray.Count)
            {
                valuesArray[entry.JsonIndex] = ConvertMarkersToNewlines(entry.Translation);
            }
        }
    }
    #endregion
}