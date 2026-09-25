using Newtonsoft.Json.Linq;

namespace ULT;

public abstract class BaseFormatHandler
{
    protected JToken jsonData;
    protected int origIndex;
    protected int transIndex;
    protected string outputPath;

    public virtual void SetJsonData(JToken data)
    {
        jsonData = data;
    }

    public virtual void SetLanguageIndices(int original, int translation)
    {
        origIndex = original;
        transIndex = translation;
    }

    public JToken GetJsonData() => jsonData;

    public abstract List<DataGridItem> LoadTerms();
    public abstract void SaveTerms(List<DataGridItem> terms);

    public static string ConvertNewlinesToMarkers(string text)
    {
        return string.IsNullOrEmpty(text) ? text :
            text.Replace("\r\n", "<cf>").Replace("\r", "<cr>").Replace("\n", "<lf>").Replace("\t", "<tb>");
    }

    public static string ConvertMarkersToNewlines(string text)
    {
        return string.IsNullOrEmpty(text) ? text :
            text.Replace("<cf>", "\r\n").Replace("<cr>", "\r").Replace("<lf>", "\n").Replace("<tb>", "\t");
    }

    public static string ReplaceBreaklines(string StringValue, bool Back = false)
    {
        if (string.IsNullOrEmpty(StringValue))
            return StringValue;

        if (!Back)
        {
            return StringValue.Replace("\r\n", "<cf>").Replace("\r", "<cr>").Replace("\n", "<lf>").Replace("\t", "<tb>");
        }
        else
        {
            return StringValue.Replace("<cf>", "\r\n").Replace("<cr>", "\r").Replace("<lf>", "\n").Replace("<tb>", "\t");
        }
    }

    protected static JToken GetTermsArray(JToken data)
    {
        return data["mSource"]?["mTerms"]?["Array"] ?? data["mTerms"]?["Array"];
    }

    protected static JToken GetLangsArray(JToken data)
    {
        return data["mSource"]?["mLanguages"]?["Array"] ?? data["mLanguages"]?["Array"];
    }

    public virtual void SetOutputPath(string path)
    {
        outputPath = path;
    }
}