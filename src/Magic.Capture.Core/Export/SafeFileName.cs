namespace Magic.Capture.Core.Export;

public static class SafeFileName
{
    private static readonly HashSet<char> InvalidCharacters = [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Magic Capture Desktop";
        var chars = input.Select(ch => ch < 32 || InvalidCharacters.Contains(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result)) result = "Magic Capture Desktop";

        var baseName = result.Split('.', 2)[0];
        if (ReservedNames.Contains(baseName)) result = "_" + result;
        return result;
    }
}
