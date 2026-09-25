using Newtonsoft.Json.Linq;

namespace ULT;

public class NonArrayFormatHandler : BaseFormatHandler
{
    private JsonFormatType formatType;
    private readonly List<DialogueEntry> dialogueEntries = new List<DialogueEntry>();

    public class DialogueEntry
    {
        public string Path { get; set; }
        public int Index { get; set; }
        public string OriginalValue { get; set; }
        public string TableName { get; set; }
        public int EntryID { get; set; }
        public string Key { get; set; }
    }

    private static readonly HashSet<string> ExcludedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Pictures", "IsPlayer", "NodeColor", "Actor", "Conversant", "Audio Files",
        "Display Name th", "Display Name tr", "Display Name de", "Display Name es", "Display Name fr",
        "Display Name it", "Display Name ja", "Display Name ko", "Display Name pl", "Display Name pt",
        "Display Name spa-M9", "Display Name zho-TW", "Description it", "Description fr", "Description ja",
        "Description es", "Description ko", "Description pl", "Description pt", "Description spa-M9",
        "Description th", "Description tr", "Description zho-TW", "Description de",
        "Is Item", "State", "Trackable", "Track", "Sequence", "it", "fr", "ja", "es", "es ", "ko",
        "pl", "pt", "spa-M9", "th", "tr", "zho-TW", "de", "Title", "Initial Value", "Entry Count"
    };

    public void SetFormatType(JsonFormatType type)
    {
        formatType = type;
    }

    public JsonFormatType GetFormatType() => formatType;

    public override List<DataGridItem> LoadTerms()
    {
        dialogueEntries.Clear();

        if (formatType == JsonFormatType.DialogueDatabase)
            return LoadDialogueDatabase();

        if (formatType == JsonFormatType.StringTables)
            return LoadStringTables();

        if (formatType == JsonFormatType.StringKeyLookup)
            return LoadStringKeyLookup();

        return new List<DataGridItem>();
    }


    public override void SaveTerms(List<DataGridItem> terms)
    {
        switch (formatType)
        {
            case JsonFormatType.DialogueDatabase:
                SaveDialogueDatabase(terms);
                break;
            case JsonFormatType.StringTables:
                SaveStringTables(terms);
                break;
            case JsonFormatType.StringKeyLookup:
                SaveStringKeyLookup(terms);
                break;
        }
    }

    #region DialogueDatabase Format
    private List<DataGridItem> LoadDialogueDatabase()
    {
        var termEntries = new List<DataGridItem>();
        FindDialogueValues(jsonData, "", 0, termEntries);
        return termEntries;
    }

    private void FindDialogueValues(JToken token, string currentPath, int currentIndex, List<DataGridItem> termEntries)
    {
        if (token is JObject obj)
        {
            if (obj["value"] != null)
            {
                string value = obj["value"].ToString();
                string title = obj["title"]?.ToString() ?? "";

                if (!ExcludedTitles.Contains(title) && !string.IsNullOrWhiteSpace(value))
                {
                    var entry = new DialogueEntry
                    {
                        Path = currentPath,
                        Index = dialogueEntries.Count,
                        OriginalValue = value
                    };
                    dialogueEntries.Add(entry);

                    termEntries.Add(new DataGridItem
                    {
                        ID = string.IsNullOrEmpty(title) ? $"Dialogue_{entry.Index}" : title,
                        Text = ConvertNewlinesToMarkers(value),
                        Translation = ConvertNewlinesToMarkers(value),
                        JsonIndex = entry.Index
                    });
                }
            }

            foreach (var prop in obj.Properties())
            {
                string newPath = string.IsNullOrEmpty(currentPath) ? prop.Name : $"{currentPath}.{prop.Name}";
                FindDialogueValues(prop.Value, newPath, currentIndex, termEntries);
            }
        }
        else if (token is JArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                FindDialogueValues(array[i], $"{currentPath}[{i}]", i, termEntries);
            }
        }
    }

    private void SaveDialogueDatabase(List<DataGridItem> terms)
    {
        for (int i = 0; i < terms.Count && i < dialogueEntries.Count; i++)
        {
            var entry = terms[i];
            var dialogueEntry = dialogueEntries[i];
            string translatedValue = ConvertMarkersToNewlines(entry.Translation);
            UpdateValueAtPath(jsonData, dialogueEntry.Path, translatedValue);
        }
    }

    private void UpdateValueAtPath(JToken root, string path, string newValue)
    {
        try
        {
            var tokens = FindTokensByPath(root, path);

            foreach (var token in tokens.OfType<JObject>().Where(obj => obj["value"] != null))
            {
                token["value"] = newValue;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating value via path {path}: {ex.Message}");
        }
    }

    private static List<JToken> FindTokensByPath(JToken root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return new List<JToken> { root };

        var parts = path.Split('.');
        var current = new List<JToken> { root };

        foreach (var part in parts)
        {
            var next = new List<JToken>();

            foreach (var token in current)
            {
                int bracketStart = part.IndexOf('[');
                if (bracketStart >= 0 && part.Contains("]"))
                {
                    string propName = part.Substring(0, bracketStart);
                    string indexStr = part.Substring(bracketStart + 1, part.IndexOf(']') - bracketStart - 1);

                    if (int.TryParse(indexStr, out int index) &&
                        token is JObject obj &&
                        obj[propName] is JArray array &&
                        index < array.Count)
                    {
                        next.Add(array[index]);
                    }
                }
                else if (token is JObject obj && obj[part] != null)
                {
                    next.Add(obj[part]);
                }
            }

            current = next;
        }

        return current;
    }
    #endregion

    #region StringTables Format
    private List<DataGridItem> LoadStringTables()
    {
        var termEntries = new List<DataGridItem>();
        var stringTables = jsonData["StringTables"] as JArray;

        if (stringTables == null) return termEntries;

        foreach (var table in stringTables)
        {
            string tableName = table["Name"]?.ToString();
            if (string.IsNullOrEmpty(tableName))
                continue;

            var entries = table["Entries"] as JArray;
            if (entries == null || entries.Count == 0)
                continue;

            foreach (var entry in entries)
            {
                try
                {
                    int id = entry["ID"]?.ToObject<int>() ?? -1;
                    string defaultText = entry["DefaultText"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(defaultText))
                        continue;

                    var dialogueEntry = new DialogueEntry
                    {
                        Index = dialogueEntries.Count,
                        OriginalValue = defaultText,
                        TableName = tableName,
                        EntryID = id
                    };
                    dialogueEntries.Add(dialogueEntry);

                    string uniqueTerm = $"{tableName}:{id}";

                    termEntries.Add(new DataGridItem
                    {
                        ID = uniqueTerm,
                        Text = ConvertNewlinesToMarkers(defaultText),
                        Translation = ConvertNewlinesToMarkers(defaultText),
                        JsonIndex = dialogueEntry.Index
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading entry: {ex.Message}");
                    continue;
                }
            }
        }

        return termEntries;
    }

    private void SaveStringTables(List<DataGridItem> terms)
    {
        var stringTables = jsonData["StringTables"] as JArray;
        if (stringTables == null) return;

        foreach (var entry in terms)
        {
            if (entry.JsonIndex < 0 || entry.JsonIndex >= dialogueEntries.Count)
                continue;

            var dialogueEntry = dialogueEntries[entry.JsonIndex];
            string translatedValue = ConvertMarkersToNewlines(entry.Translation);

            foreach (var table in stringTables)
            {
                string tableName = table["Name"]?.ToString();
                if (tableName != dialogueEntry.TableName)
                    continue;

                var entries = table["Entries"] as JArray;
                if (entries == null)
                    continue;

                foreach (var tableEntry in entries)
                {
                    int id = tableEntry["ID"]?.ToObject<int>() ?? -1;
                    if (id == dialogueEntry.EntryID)
                    {
                        tableEntry["DefaultText"] = translatedValue;
                        break;
                    }
                }

                break;
            }
        }
    }
    #endregion

    #region StringKeyLookup Format
    private List<DataGridItem> LoadStringKeyLookup()
    {
        var termEntries = new List<DataGridItem>();
        var stringKeyLookup = jsonData["_stringKeyLookup"] as JObject;

        if (stringKeyLookup == null) return termEntries;

        foreach (var property in stringKeyLookup.Properties())
        {
            string key = property.Name;
            var entry = property.Value as JObject;

            if (entry == null) continue;

            string text = entry["_localizations"]?["EN"]?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(text))
            {
                text = entry["_fallbackString"]?.ToString() ?? "";
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                var dialogueEntry = new DialogueEntry
                {
                    Index = dialogueEntries.Count,
                    OriginalValue = text,
                    TableName = "_stringKeyLookup",
                    EntryID = -1,
                    Key = key
                };
                dialogueEntries.Add(dialogueEntry);

                termEntries.Add(new DataGridItem
                {
                    ID = key,
                    Text = ConvertNewlinesToMarkers(text),
                    Translation = ConvertNewlinesToMarkers(text),
                    JsonIndex = dialogueEntry.Index
                });
            }
        }

        return termEntries;
    }

    private void SaveStringKeyLookup(List<DataGridItem> terms)
    {
        var stringKeyLookup = jsonData["_stringKeyLookup"] as JObject;
        if (stringKeyLookup == null) return;
        foreach (var entry in terms)
        {
            if (entry.JsonIndex < 0 || entry.JsonIndex >= dialogueEntries.Count)
                continue;

            string translatedValue = ConvertMarkersToNewlines(entry.Translation);

            if (stringKeyLookup[entry.ID] is JObject keyEntry)
            {
                if (keyEntry["_localizations"] is JObject localizations)
                {
                    localizations["EN"] = translatedValue;
                }

                keyEntry["_fallbackString"] = translatedValue;
            }
        }
    }
    #endregion
}