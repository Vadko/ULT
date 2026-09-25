using System.IO;

namespace ULT;

public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hikaro", "ULT", "settings.txt");

    public static bool CreateStatusFiles { get; private set; } = true;
    public static bool DiscordPresenceEnabled { get; private set; } = true;
    public static string OriginalTextAlignment { get; private set; } = "Center";
    public static int OriginalTextFontSize { get; private set; } = 12;
    public static int EditTextFontSize { get; private set; } = 14;
    public static int MaxTranslationMemoryVariants { get; private set; } = 5;

    public static void Load()
    {
        if (!File.Exists(SettingsPath)) return;

        foreach (var line in File.ReadAllLines(SettingsPath))
        {
            var parts = line.Split('=');
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();

            if (key == "CreateStatusFiles")
                CreateStatusFiles = value == "true";

            if (key == "DiscordPresenceEnabled")
                DiscordPresenceEnabled = value == "true";

            if (key == "OriginalTextAlignment" && (value == "Left" || value == "Center" || value == "Right"))
                OriginalTextAlignment = value;

            if (key == "OriginalTextFontSize" && int.TryParse(value, out var size) && size >= 8 && size <= 14)
                OriginalTextFontSize = size;

            if (key == "EditTextFontSize" && int.TryParse(value, out var editSize) && editSize >= 8 && editSize <= 14)
                EditTextFontSize = editSize;

            if (key == "MaxTranslationMemoryVariants" && int.TryParse(value, out var maxVariants) && maxVariants >= 3 && maxVariants <= 8)
                MaxTranslationMemoryVariants = maxVariants;
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllLines(SettingsPath,
        [
            $"CreateStatusFiles={CreateStatusFiles.ToString().ToLower()}",
            $"DiscordPresenceEnabled={DiscordPresenceEnabled.ToString().ToLower()}",
            $"OriginalTextAlignment={OriginalTextAlignment}",
            $"OriginalTextFontSize={OriginalTextFontSize}",
            $"EditTextFontSize={EditTextFontSize}",
            $"MaxTranslationMemoryVariants={MaxTranslationMemoryVariants}",
        ]);
    }

    public static void SetCreateStatusFiles(bool value) { CreateStatusFiles = value; Save(); }
    public static void SetDiscordPresenceEnabled(bool value) { DiscordPresenceEnabled = value; Save(); }
    public static void SetOriginalTextAlignment(string value) { OriginalTextAlignment = value; Save(); }
    public static void SetOriginalTextFontSize(int value) { OriginalTextFontSize = value; Save(); }
    public static void SetEditTextFontSize(int value) { EditTextFontSize = value; Save(); }
    public static void SetMaxTranslationMemoryVariants(int value) { MaxTranslationMemoryVariants = Math.Clamp(value, 3, 8); Save(); }
}