namespace Magic.Capture.Core.History;

public sealed record HistoryMetadataUpdate(
    string? Title,
    string? Notes,
    IReadOnlyList<string> Tags,
    bool? IsFavorite = null);

public static class HistoryMetadata
{
    public const int MaxTitleLength = 240;
    public const int MaxNotesLength = 4096;
    public const int MaxTags = 32;
    public const int MaxTagLength = 64;
    public const int MaxSessionIdLength = 96;

    public static HistoryMetadataUpdate Normalize(
        string? title,
        string? notes,
        IEnumerable<string>? tags,
        bool? isFavorite = null)
    {
        var normalizedTags = (tags ?? [])
            .Select(value => NormalizeText(value, MaxTagLength))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToArray();

        return new HistoryMetadataUpdate(
            NormalizeText(title, MaxTitleLength),
            NormalizeText(notes, MaxNotesLength),
            normalizedTags,
            isFavorite);
    }

    public static string? NormalizeSessionId(string? value) => NormalizeText(value, MaxSessionIdLength);

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
