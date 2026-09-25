using System.IO;
using System.Text.Json;

namespace ULT;

public static class JSONHelper
{
    public record JsonEntry(string Id, string Text);

    public static List<JsonEntry> Parse(string filePath)
    {
        var result = new List<JsonEntry>();

        string rawJson = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Очікується JSON-об’єкт верхнього рівня.");

        foreach (var nsProperty in root.EnumerateObject())
        {
            string ns = nsProperty.Name;
            if (nsProperty.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var keyProperty in nsProperty.Value.EnumerateObject())
            {
                string key = keyProperty.Name;
                if (keyProperty.Value.ValueKind != JsonValueKind.String)
                    continue;

                string value = keyProperty.Value.GetString() ?? "";
                value = BaseFormatHandler.ReplaceBreaklines(value);

                string id = string.IsNullOrEmpty(ns) ? key : $"{ns}::{key}";
                result.Add(new JsonEntry(id, value));
            }
        }

        return result;
    }
}