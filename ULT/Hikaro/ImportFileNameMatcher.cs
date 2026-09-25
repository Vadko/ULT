namespace ULT;

internal static class ImportFileNameMatcher
{
    public static bool IsMatch(string targetBase, string sourceBase)
    {
        if (string.Equals(targetBase, sourceBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(Normalize(targetBase), Normalize(sourceBase), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        if (name.StartsWith("N_", StringComparison.OrdinalIgnoreCase))
            name = name[2..];

        name = TruncateAtCab(name);

        int searchFrom = name.Length;
        int lastUnderscore = name.LastIndexOf('_', searchFrom - 1);
        if (lastUnderscore > 0 && IsDigitsOnly(name[(lastUnderscore + 1)..]))
            searchFrom = lastUnderscore;

        int nUnderscore = name.LastIndexOf('_', searchFrom - 1);
        if (nUnderscore > 0)
        {
            string segment = name[(nUnderscore + 1)..searchFrom];
            if (segment.Equals("N", StringComparison.OrdinalIgnoreCase))
                name = name[..nUnderscore];
        }

        name = StripTrailingNumericSuffix(name);

        return name;
    }

    private static string TruncateAtCab(string name)
    {
        var segments = name.Split('-');
        int cabIndex = Array.FindIndex(segments, s => s.Equals("CAB", StringComparison.OrdinalIgnoreCase));
        if (cabIndex <= 0) return name;
        return string.Join('-', segments.Take(cabIndex));
    }

    private static string StripTrailingNumericSuffix(string name)
    {
        int lastDash = name.LastIndexOf('-');
        if (lastDash > 0 && IsDigitsOnly(name[(lastDash + 1)..]))
            name = name[..lastDash];
        return name;
    }

    private static bool IsDigitsOnly(string s)
        => !string.IsNullOrEmpty(s) && s.All(char.IsDigit);
}