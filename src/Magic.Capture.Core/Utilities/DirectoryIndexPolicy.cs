namespace Magic.Capture.Core.Utilities;

public static class DirectoryIndexPolicy
{
    public const int MaximumEntries = 100_000;
    public const int MaximumDepth = 64;
    public const int MaximumDisplayNameCharacters = 512;
    public const int MaximumOutputCharacters = 16 * 1024 * 1024;

    public static string NormalizeDisplayName(string? value)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        if (normalized.Length <= MaximumDisplayNameCharacters) return normalized;
        return normalized[..(MaximumDisplayNameCharacters - 1)] + "…";
    }

    public static bool CanAppend(int currentLength, int additionalCharacters) =>
        currentLength >= 0 &&
        additionalCharacters >= 0 &&
        currentLength <= MaximumOutputCharacters &&
        additionalCharacters <= MaximumOutputCharacters - currentLength;
}
