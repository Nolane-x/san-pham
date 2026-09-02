namespace Magic.Capture.Core.History;

public sealed record HistoryMaintenancePlan(
    IReadOnlyList<Guid> RowsWithoutPrimary,
    IReadOnlyList<string> OrphanPrimaryPaths,
    IReadOnlyList<Guid> MissingThumbnailItemIds,
    IReadOnlyList<string> OrphanThumbnailPaths,
    IReadOnlyList<Guid> MissingFingerprintItemIds)
{
    public int IssueCount => RowsWithoutPrimary.Count + OrphanPrimaryPaths.Count + MissingThumbnailItemIds.Count + OrphanThumbnailPaths.Count + MissingFingerprintItemIds.Count;
}

public static class HistoryMaintenance
{
    public static HistoryMaintenancePlan Plan(
        IEnumerable<HistoryItem>? items,
        IEnumerable<string>? existingPrimaryPaths,
        IEnumerable<string>? existingThumbnailPaths,
        IEnumerable<string>? allThumbnailPaths = null)
    {
        var rows = (items ?? []).Where(item => item is not null).ToArray();
        var primaries = new HashSet<string>((existingPrimaryPaths ?? []).Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        var thumbnails = new HashSet<string>((existingThumbnailPaths ?? []).Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        if (allThumbnailPaths is not null) thumbnails.UnionWith(allThumbnailPaths.Select(NormalizePath));

        var expectedPrimary = rows.Select(item => NormalizePath(item.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedThumbnail = rows
            .Where(item => HistoryThumbnailPolicy.ShouldPreGenerate(item.Width, item.Height))
            .Select(ExpectedThumbnailPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rowsWithoutPrimary = rows.Where(item => !primaries.Contains(NormalizePath(item.RelativePath))).Select(item => item.Id).Distinct().OrderBy(id => id).ToArray();
        var orphanPrimary = primaries.Where(path => !expectedPrimary.Contains(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var missingThumbs = rows.Where(item =>
                HistoryThumbnailPolicy.ShouldPreGenerate(item.Width, item.Height) &&
                !thumbnails.Contains(ExpectedThumbnailPath(item)))
            .Select(item => item.Id).Distinct().OrderBy(id => id).ToArray();
        var orphanThumbs = thumbnails.Where(path => !expectedThumbnail.Contains(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var missingFingerprints = rows.Where(item => !HistoryDuplicateIndex.IsSha256(item.ContentSha256) || !item.PerceptualHash64.HasValue)
            .Select(item => item.Id).Distinct().OrderBy(id => id).ToArray();

        return new HistoryMaintenancePlan(rowsWithoutPrimary, orphanPrimary, missingThumbs, orphanThumbs, missingFingerprints);
    }

    public static string ExpectedThumbnailPath(HistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var primary = NormalizePath(item.RelativePath);
        var directory = Path.GetDirectoryName(primary.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var name = item.Id.ToString("N") + ".thumb.png";
        return NormalizePath(Path.Combine(directory, name));
    }

    private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
}
