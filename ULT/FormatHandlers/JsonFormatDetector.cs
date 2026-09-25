using Newtonsoft.Json.Linq;

namespace ULT;

public enum JsonFormatType
{
    Unknown,
    TermsArray,      
    ItemsArray,       
    LinesArray,    
    TableDataArray,  
    LanguageKeysArray,  
    GameStringsArray,
    DialogueDatabase,  
    StringTables,     
    StringKeyLookup,    
    LocaKeysLocaValues, 
    TextContentsArray,  
    TinyTextAsset,    
    PassagesArray,  
    NamedDataArray,    
    LocalizationData,   
    KeysValuesTable  
}

public static class JsonFormatDetector
{
    public static JsonFormatType DetectFormat(JToken data)
    {
        if (data["passages"]?["Array"] is JArray passages && passages.Count > 0)
        {
            var first = passages[0];
            if (first["id"] != null && first["text"] != null)
                return JsonFormatType.PassagesArray;
        }

        if ((data["m_Text"] != null || data["m_text"] != null) && data["m_GameObject"]?["m_PathID"] != null)
        {
            return JsonFormatType.TinyTextAsset;
        }

        if ((data["textContents"]?["Array"] ?? data["configList"]?["Array"]) is JArray textContents &&
            textContents.Count > 0)
        {
            var firstItem = textContents[0];
            if (firstItem["id"] != null && firstItem["text"] != null)
            {
                return JsonFormatType.TextContentsArray;
            }
        }

        if (data["mLocaKeys"]?["Array"] is JArray locaKeys &&
            data["mLocaValues"]?["Array"] is JArray locaValues &&
            locaKeys.Count == locaValues.Count &&
            locaKeys.Count > 0)
        {
            return JsonFormatType.LocaKeysLocaValues;
        }

        if (data["m_data"]?["m_keys"]?["Array"] is JArray keysArr &&
            data["m_data"]?["m_values"]?["Array"] is JArray valuesArr &&
            keysArr.Count == valuesArr.Count &&
            keysArr.Count > 0)
        {
            return JsonFormatType.KeysValuesTable;
        }

        if (data["m_languageKeys"]?["Array"] is JArray &&
            data["m_languageValues"]?["Array"] is JArray &&
            data["m_fieldValues"]?["Array"] is JArray)
        {
            return JsonFormatType.LanguageKeysArray;
        }

        if (data["m_TableData"]?["Array"] is JArray &&
            data["m_LocaleId"]?["m_Code"] != null)
        {
            return JsonFormatType.TableDataArray;
        }

        if (data["StringTables"] is JArray)
        {
            return JsonFormatType.StringTables;
        }

        if (data["_stringKeyLookup"] is JObject)
        {
            return JsonFormatType.StringKeyLookup;
        }

        if (HasDialogueEntry(data))
            return JsonFormatType.DialogueDatabase;

        if (data["Items"] is JArray && data["Languages"] is JArray)
        {
            return JsonFormatType.ItemsArray;
        }

        if (data["lines"]?["Array"] is JArray && data["languages"]?["Array"] is JArray)
        {
            return JsonFormatType.LinesArray;
        }

        if (data["dataArray"]?["Array"] is JArray namedDataArray && namedDataArray.Count > 0)
        {
            var firstItem = namedDataArray[0] as JObject;
            if (firstItem != null && firstItem["name"] != null && firstItem["key"] == null)
            {
                return JsonFormatType.NamedDataArray;
            }
        }

        if (data["dataArray"]?["Array"] is JArray dataArray && dataArray.Count > 0)
        {
            var firstItem = dataArray[0] as JObject;
            if (firstItem != null && firstItem["key"] != null && firstItem["comments"] != null)
            {
                return JsonFormatType.GameStringsArray;
            }
        }

        if (data["KeyList"]?["Array"] is JArray keyList && data["SheetDataList"]?["Array"] is JArray sheetDataList && keyList.Count > 0 && sheetDataList.Count > 0)
        {
            return JsonFormatType.LocalizationData;
        }

        var termsArray = GetTermsArray(data);
        var langsArray = GetLangsArray(data);
        if (termsArray is JArray && langsArray is JArray)
        {
            return JsonFormatType.TermsArray;
        }

        return JsonFormatType.Unknown;
    }

    private static JToken GetTermsArray(JToken data)
    {
        return data["mSource"]?["mTerms"]?["Array"] ?? data["mTerms"]?["Array"];
    }

    private static JToken GetLangsArray(JToken data)
    {
        return data["mSource"]?["mLanguages"]?["Array"] ?? data["mLanguages"]?["Array"];
    }

    public static bool IsArrayFormat(JsonFormatType format)
    {
        return format == JsonFormatType.TermsArray ||
               format == JsonFormatType.ItemsArray ||
               format == JsonFormatType.LinesArray ||
               format == JsonFormatType.TableDataArray ||
               format == JsonFormatType.LanguageKeysArray ||
               format == JsonFormatType.GameStringsArray ||
               format == JsonFormatType.LocaKeysLocaValues ||
               format == JsonFormatType.TextContentsArray ||
               format == JsonFormatType.TinyTextAsset ||
               format == JsonFormatType.PassagesArray ||
               format == JsonFormatType.NamedDataArray ||
               format == JsonFormatType.LocalizationData ||
               format == JsonFormatType.KeysValuesTable;
    }

    public static bool IsDialogueFormat(JsonFormatType format)
    {
        return format == JsonFormatType.DialogueDatabase ||
               format == JsonFormatType.StringTables ||
               format == JsonFormatType.StringKeyLookup;
    }

    private static bool HasDialogueEntry(JToken token)
    {
        if (token is JObject obj && obj["title"] != null && obj["value"] != null)
            return true;

        foreach (var child in token.Children())
        {
            if (HasDialogueEntry(child))
                return true;
        }

        return false;
    }
}