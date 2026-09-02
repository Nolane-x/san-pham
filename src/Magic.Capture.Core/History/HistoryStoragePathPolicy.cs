namespace Magic.Capture.Core.History;

/// <summary>
/// Validates the filename identity invariant for History-owned image files. Root containment is
/// enforced separately by LocalPathGuard; this policy prevents a tampered index from making a
/// capture record point at another app-owned file inside the History root.
/// </summary>
public static class HistoryStoragePathPolicy
{
    public static bool IsExpectedPrimary(Guid id, string? relativePath) =>
        MatchesFileName(relativePath, id.ToString("N") + ".png");

    public static bool IsExpectedThumbnail(Guid id, string? relativePath) =>
        MatchesFileName(relativePath, id.ToString("N") + ".thumb.png");

    private static bool MatchesFileName(string? relativePath, string expectedFileName)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0) return false;
        var slash = normalized.LastIndexOf('/');
        var fileName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        return string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase);
    }

}
