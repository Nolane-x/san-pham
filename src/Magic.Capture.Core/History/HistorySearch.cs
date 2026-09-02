using System.Globalization;

namespace Magic.Capture.Core.History;

public static class HistorySearch
{
    public static bool Matches(HistoryItem item, string? query)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(query)) return true;

        var searchable = GetSearchableText(item);

        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!searchable.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static string GetSearchableText(HistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.Join('\n', new[]
        {
            item.Title ?? string.Empty,
            item.Notes ?? string.Empty,
            item.Tags is null ? string.Empty : string.Join(' ', item.Tags),
            item.SourceKind,
            item.SourceDisplayName ?? string.Empty,
            item.WindowTitle ?? string.Empty,
            item.ProcessName ?? string.Empty,
            item.MonitorName ?? string.Empty,
            item.SessionId ?? string.Empty,
            item.RelativePath,
            item.OcrPreview ?? string.Empty,
            item.BarcodePreview ?? string.Empty,
            item.IsFavorite ? "favorite favourite starred" : string.Empty,
            $"{item.Width}x{item.Height}",
            $"{item.Width} × {item.Height}",
            item.CreatedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        });
    }
}
