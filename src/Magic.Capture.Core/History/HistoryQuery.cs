namespace Magic.Capture.Core.History;

public enum HistorySortOrder
{
    Newest,
    Oldest,
    FileSizeDescending,
    FileSizeAscending,
    MostUsed
}

public sealed record HistoryQueryOptions(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? SourceOrAppContains = null,
    string? WindowContains = null,
    string? MonitorContains = null,
    string? CaptureType = null,
    int? MinWidth = null,
    int? MaxWidth = null,
    int? MinHeight = null,
    int? MaxHeight = null,
    bool? HasOcr = null,
    bool? HasBarcode = null,
    bool? IsFavorite = null,
    string? SessionId = null,
    HistorySortOrder Sort = HistorySortOrder.Newest,
    string? WorkspaceId = null,
    string? FolderId = null,
    string? CollectionId = null,
    string? WorkflowId = null,
    string? AiActionId = null);

public static class HistoryQuery
{
    public const int MaximumResults = 100_000;
    private const int MaximumFilterTextLength = 240;

    public static IReadOnlyList<HistoryItem> Apply(IEnumerable<HistoryItem>? source, HistoryQueryOptions? options, HistoryLibrarySnapshot? library = null)
    {
        options ??= new HistoryQueryOptions();
        var from = options.FromUtc;
        var to = options.ToUtc;
        if (from is not null && to is not null && from > to) (from, to) = (to, from);

        var sourceOrApp = NormalizeFilter(options.SourceOrAppContains);
        var window = NormalizeFilter(options.WindowContains);
        var monitor = NormalizeFilter(options.MonitorContains);
        var captureType = NormalizeFilter(options.CaptureType);
        var session = NormalizeFilter(options.SessionId);
        var workspaceId = NormalizeFilter(options.WorkspaceId);
        var folderId = NormalizeFilter(options.FolderId);
        var collectionId = NormalizeFilter(options.CollectionId);
        var workflowId = NormalizeFilter(options.WorkflowId);
        var aiActionId = NormalizeFilter(options.AiActionId);
        var librarySnapshot = HistoryLibraryPolicy.Normalize(library);
        var libraryByAsset = librarySnapshot.Assets.ToDictionary(x => x.AssetId);
        var minWidth = NormalizeDimension(options.MinWidth);
        var maxWidth = NormalizeDimension(options.MaxWidth);
        var minHeight = NormalizeDimension(options.MinHeight);
        var maxHeight = NormalizeDimension(options.MaxHeight);
        if (minWidth is not null && maxWidth is not null && minWidth > maxWidth) (minWidth, maxWidth) = (maxWidth, minWidth);
        if (minHeight is not null && maxHeight is not null && minHeight > maxHeight) (minHeight, maxHeight) = (maxHeight, minHeight);

        IEnumerable<HistoryItem> query = source ?? [];
        query = query.Where(item =>
            (from is null || item.CreatedUtc >= from) &&
            (to is null || item.CreatedUtc <= to) &&
            MatchesSourceOrApp(item, sourceOrApp) &&
            Contains(item.WindowTitle, window) &&
            Contains(item.MonitorName, monitor) &&
            (captureType is null || string.Equals(item.SourceKind, captureType, StringComparison.OrdinalIgnoreCase)) &&
            (minWidth is null || item.Width >= minWidth) &&
            (maxWidth is null || item.Width <= maxWidth) &&
            (minHeight is null || item.Height >= minHeight) &&
            (maxHeight is null || item.Height <= maxHeight) &&
            (options.HasOcr is null || HasText(item.OcrPreview) == options.HasOcr.Value) &&
            (options.HasBarcode is null || HasText(item.BarcodePreview) == options.HasBarcode.Value) &&
            (options.IsFavorite is null || item.IsFavorite == options.IsFavorite.Value) &&
            (session is null || string.Equals(item.SessionId, session, StringComparison.OrdinalIgnoreCase)) &&
            MatchesLibrary(item.Id, libraryByAsset, workspaceId, folderId, collectionId, workflowId, aiActionId));

        query = options.Sort switch
        {
            HistorySortOrder.Oldest => query.OrderBy(item => item.CreatedUtc).ThenBy(item => item.Id),
            HistorySortOrder.FileSizeDescending => query.OrderByDescending(item => item.FileBytes).ThenByDescending(item => item.CreatedUtc),
            HistorySortOrder.FileSizeAscending => query.OrderBy(item => item.FileBytes).ThenByDescending(item => item.CreatedUtc),
            HistorySortOrder.MostUsed => query.OrderByDescending(item => libraryByAsset.TryGetValue(item.Id, out var activity) ? activity.UseCount : 0)
                .ThenByDescending(item => libraryByAsset.TryGetValue(item.Id, out var activity) ? activity.LastUsedUtc ?? DateTimeOffset.MinValue : DateTimeOffset.MinValue)
                .ThenByDescending(item => item.CreatedUtc),
            _ => query.OrderByDescending(item => item.CreatedUtc).ThenBy(item => item.Id)
        };

        return query.Take(MaximumResults).ToArray();
    }

    private static bool MatchesLibrary(
        Guid assetId,
        IReadOnlyDictionary<Guid, HistoryAssetLibraryRecord> library,
        string? workspaceId, string? folderId, string? collectionId, string? workflowId, string? aiActionId)
    {
        if (workspaceId is null && folderId is null && collectionId is null && workflowId is null && aiActionId is null) return true;
        if (!library.TryGetValue(assetId, out var record)) return false;
        return (workspaceId is null || string.Equals(record.WorkspaceId, workspaceId, StringComparison.Ordinal)) &&
               (folderId is null || string.Equals(record.FolderId, folderId, StringComparison.Ordinal)) &&
               (collectionId is null || (record.CollectionIds?.Contains(collectionId, StringComparer.Ordinal) ?? false)) &&
               (workflowId is null || (record.WorkflowIds?.Contains(workflowId, StringComparer.OrdinalIgnoreCase) ?? false)) &&
               (aiActionId is null || (record.AiActionIds?.Contains(aiActionId, StringComparer.OrdinalIgnoreCase) ?? false));
    }

    private static bool MatchesSourceOrApp(HistoryItem item, string? filter) =>
        filter is null || Contains(item.SourceDisplayName, filter) || Contains(item.ProcessName, filter) || Contains(item.SourceKind, filter);

    private static bool Contains(string? value, string? filter) =>
        filter is null || (!string.IsNullOrWhiteSpace(value) && value.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static int? NormalizeDimension(int? value) => value is > 0 and <= 100_000 ? value : null;

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= MaximumFilterTextLength ? normalized : normalized[..MaximumFilterTextLength];
    }
}
