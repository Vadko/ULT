using Newtonsoft.Json.Linq;
using System.IO;

namespace ULT;

public static class JsonImportHelper
{
    public static bool NeedsLanguageSelection(JsonFormatType format)
    {
        return format == JsonFormatType.TermsArray ||
               format == JsonFormatType.ItemsArray ||
               format == JsonFormatType.LinesArray ||
               format == JsonFormatType.LanguageKeysArray ||
               format == JsonFormatType.GameStringsArray ||
               format == JsonFormatType.NamedDataArray ||
               format == JsonFormatType.LocalizationData;
    }

    public static JArray GetLanguagesArray(JToken importData, JsonFormatType format)
    {
        switch (format)
        {
            case JsonFormatType.TermsArray:
                return (importData["mSource"]?["mLanguages"]?["Array"] as JArray)
                    ?? (importData["mLanguages"]?["Array"] as JArray);

            case JsonFormatType.ItemsArray:
                return importData["Languages"] as JArray;

            case JsonFormatType.LinesArray:
                return importData["languages"]?["Array"] as JArray;

            case JsonFormatType.LanguageKeysArray:
                return importData["m_languageKeys"]?["Array"] as JArray;

            case JsonFormatType.GameStringsArray:
                {
                    var dataArray = importData["dataArray"]?["Array"] as JArray;
                    if (dataArray == null || dataArray.Count == 0) return null;
                    if (dataArray[0] is not JObject firstItem) return null;

                    var langs = new JArray();
                    foreach (var prop in firstItem.Properties())
                    {
                        if (prop.Name != "key" &&
                            prop.Name != "category" &&
                            prop.Name != "subcategory" &&
                            prop.Name != "comments")
                        {
                            langs.Add(prop.Name);
                        }
                    }
                    return langs.Count > 0 ? langs : null;
                }

            case JsonFormatType.NamedDataArray:
                {
                    var dataArray = importData["dataArray"]?["Array"] as JArray;
                    if (dataArray == null || dataArray.Count == 0) return null;
                    if (dataArray[0] is not JObject firstItem) return null;

                    var langs = new JArray();
                    foreach (var prop in firstItem.Properties())
                    {
                        if (prop.Name != "name")
                            langs.Add(prop.Name);
                    }
                    return langs.Count > 0 ? langs : null;
                }

            case JsonFormatType.LocalizationData:
                {
                    var sheets = importData["SheetDataList"]?["Array"] as JArray;
                    if (sheets == null) return null;
                    foreach (var sheet in sheets)
                    {
                        var dl = sheet["DataList"]?["Array"] as JArray;
                        if (dl != null && dl.Count > 1)
                        {
                            int langCount = dl.Count - 1;
                            var langs = new JArray();
                            for (int i = 0; i < langCount; i++)
                                langs.Add($"Language {i + 1}");
                            return langs;
                        }
                    }
                    return null;
                }

            default:
                return null;
        }
    }

    public static List<DataGridItem> ExtractFromJson(JToken importData, JsonFormatType format, int importLangIndex = 0)
    {
        if (JsonFormatDetector.IsArrayFormat(format))
        {
            var handler = new ArrayFormatHandler();
            handler.SetFormatType(format);
            handler.SetJsonData(importData);
            handler.SetLanguageIndices(importLangIndex, importLangIndex);
            return handler.LoadTerms();
        }
        else
        {
            var handler = new NonArrayFormatHandler();
            handler.SetFormatType(format);
            handler.SetJsonData(importData);
            return handler.LoadTerms();
        }
    }

    public static int ApplyImport(IList<DataGridItem> currentTerms, List<DataGridItem> importedTerms)
    {
        var termQueues = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        foreach (var imported in importedTerms)
        {
            if (imported.ID == null) continue;

            if (!termQueues.TryGetValue(imported.ID, out var q))
                termQueues[imported.ID] = q = new Queue<string>();

            q.Enqueue(imported.Translation ?? "");
        }

        int matched = 0;
        foreach (var current in currentTerms)
        {
            if (current.ID == null) continue;
            if (!termQueues.TryGetValue(current.ID, out var q) || q.Count == 0) continue;

            string incoming = q.Dequeue();

            if (string.IsNullOrWhiteSpace(incoming) || incoming == current.Translation)
                continue;

            current.Translation = incoming;
            current.IsModified = current.Translation != current.Text;
            matched++;
        }

        return matched;
    }

    public static List<(string tabPath, string importFile)> BuildBatchImportPlan(IEnumerable<string> tabPaths, IEnumerable<string> candidateFiles)
    {
        var result = new List<(string, string)>();
        var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = candidateFiles.ToList();

        foreach (var tabPath in tabPaths)
        {
            string tabFileName = Path.GetFileName(tabPath);
            string tabBase = Path.GetFileNameWithoutExtension(tabPath);

            string best = null;
            int bestPriority = int.MaxValue;

            foreach (var candidate in candidates)
            {
                if (usedFiles.Contains(candidate)) continue;

                string candFileName = Path.GetFileName(candidate);
                string candBase = Path.GetFileNameWithoutExtension(candidate);

                int priority;

                if (string.Equals(candFileName, tabFileName, StringComparison.OrdinalIgnoreCase))
                    priority = 1;
                else if (string.Equals(candBase, tabBase, StringComparison.OrdinalIgnoreCase))
                    priority = 2;
                else if (ImportFileNameMatcher.IsMatch(tabBase, candBase))
                    priority = 3;
                else
                    continue;

                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    best = candidate;
                }
            }

            if (best != null)
            {
                result.Add((tabPath, best));
                usedFiles.Add(best);
            }
        }

        return result;
    }

    public static void ApplyImportedTranslations(IList<DataGridItem> rows, List<DataGridItem> updatedRows)
    {
        if (updatedRows == null) return;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Translation != updatedRows[i].Translation)
                rows[i].Translation = updatedRows[i].Translation;
        }
    }

    #region Importing to Original
    public static int ApplyImportToOriginal(IList<DataGridItem> currentTerms, List<DataGridItem> importedTerms)
    {
        var termQueues = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        foreach (var imported in importedTerms)
        {
            if (imported.ID == null) continue;

            if (!termQueues.TryGetValue(imported.ID, out var q))
                termQueues[imported.ID] = q = new Queue<string>();

            q.Enqueue(imported.Text ?? "");
        }

        int matched = 0;
        foreach (var current in currentTerms)
        {
            if (current.ID == null) continue;
            if (!termQueues.TryGetValue(current.ID, out var q) || q.Count == 0) continue;

            string incoming = q.Dequeue();
            if (string.IsNullOrWhiteSpace(incoming) || incoming == current.Text) continue;

            current.Text = incoming;

            if (current.Status != RowStatus.NeedsReview)
                current.Status = RowStatus.NeedsReview;

            matched++;
        }

        return matched;
    }

    public static (int changedCount, List<DataGridItem> updatedRows) ImportOriginalFromJson(IList<DataGridItem> rows, JToken importData, JsonFormatType format, int importLangIndex = 0)
    {
        if (format == JsonFormatType.Unknown) return (0, null);

        var imported = ExtractFromJson(importData, format, importLangIndex);

        var termRows = rows.Select(r => new DataGridItem
        {
            ID = r.ID,
            Text = r.Text,
            Status = r.Status
        }).ToList();

        int changed = ApplyImportToOriginal(termRows, imported);
        return (changed, termRows);
    }

    public static void ApplyImportedOriginals(IList<DataGridItem> rows, List<DataGridItem> updatedRows)
    {
        if (updatedRows == null) return;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Text != updatedRows[i].Text)
                rows[i].Text = updatedRows[i].Text;

            rows[i].IsModified = rows[i].Translation != rows[i].Text;

            if (rows[i].Status != updatedRows[i].Status)
                rows[i].Status = updatedRows[i].Status;
        }
    }
    #endregion

    public static (JToken importData, JsonFormatType format) PrepareJsonImport(string importFilePath)
    {
        var importData = JToken.Parse(File.ReadAllText(importFilePath));
        var format = JsonFormatDetector.DetectFormat(importData);
        return (importData, format);
    }

    public static (int changedCount, List<DataGridItem> updatedRows) ImportFromJson(IList<DataGridItem> rows, JToken importData, JsonFormatType format, int importLangIndex = 0)
    {
        if (format == JsonFormatType.Unknown) return (0, null);

        var imported = ExtractFromJson(importData, format, importLangIndex);

        var termRows = rows.Select(r => new DataGridItem
        {
            ID = r.ID,
            Text = r.Text,
            Translation = r.Translation
        }).ToList();

        int changed = ApplyImport(termRows, imported);
        return (changed, termRows);
    }
}