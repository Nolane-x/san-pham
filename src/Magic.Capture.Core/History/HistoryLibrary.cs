namespace Magic.Capture.Core.History;

public sealed record HistoryWorkspace(string Id, string Name, DateTimeOffset CreatedUtc);
public sealed record HistoryFolder(string Id, string WorkspaceId, string Name, DateTimeOffset CreatedUtc);
public sealed record HistoryCollection(string Id, string Name, DateTimeOffset CreatedUtc);

public sealed record HistoryAssetLibraryRecord(
    Guid AssetId,
    string? WorkspaceId = null,
    string? FolderId = null,
    IReadOnlyList<string>? CollectionIds = null,
    int UseCount = 0,
    DateTimeOffset? LastUsedUtc = null,
    IReadOnlyList<string>? WorkflowIds = null,
    IReadOnlyList<string>? AiActionIds = null);

public sealed record HistoryLibrarySnapshot(
    int SchemaVersion,
    IReadOnlyList<HistoryWorkspace> Workspaces,
    IReadOnlyList<HistoryFolder> Folders,
    IReadOnlyList<HistoryCollection> Collections,
    IReadOnlyList<HistoryAssetLibraryRecord> Assets)
{
    public static HistoryLibrarySnapshot Empty { get; } = new(1, [], [], [], []);
}

public static class HistoryLibraryPolicy
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumWorkspaces = 32;
    public const int MaximumFolders = 128;
    public const int MaximumFoldersPerWorkspace = 64;
    public const int MaximumCollections = 128;
    public const int MaximumCollectionMembers = 5000;
    public const int MaximumCollectionsPerAsset = 32;
    public const int MaximumWorkflowIdsPerAsset = 32;
    public const int MaximumAiActionIdsPerAsset = 32;
    public const int MaximumUseCount = 1000000;
    public const int MaximumNameLength = 120;
    public const int MaximumActivityIdLength = 160;

    public static HistoryLibrarySnapshot Normalize(HistoryLibrarySnapshot? input)
    {
        if (input is null) return HistoryLibrarySnapshot.Empty;

        var workspaces = NormalizeWorkspaces(input.Workspaces);
        var workspaceIds = workspaces.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        var folders = NormalizeFolders(input.Folders, workspaceIds);
        var folderById = folders.ToDictionary(x => x.Id, StringComparer.Ordinal);

        var collections = NormalizeCollections(input.Collections);
        var collectionIds = collections.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        var assets = new List<HistoryAssetLibraryRecord>();
        foreach (var raw in (input.Assets ?? []).Where(x => x.AssetId != Guid.Empty).GroupBy(x => x.AssetId).Select(g => g.Last()))
        {
            var workspaceId = NormalizeId(raw.WorkspaceId);
            if (workspaceId is not null && !workspaceIds.Contains(workspaceId)) workspaceId = null;

            var folderId = NormalizeId(raw.FolderId);
            if (folderId is not null)
            {
                if (!folderById.TryGetValue(folderId, out var folder)) folderId = null;
                else if (workspaceId is null || !string.Equals(folder.WorkspaceId, workspaceId, StringComparison.Ordinal)) folderId = null;
            }

            var memberships = NormalizeIds(raw.CollectionIds, MaximumCollectionsPerAsset)
                .Where(collectionIds.Contains)
                .Take(MaximumCollectionsPerAsset)
                .ToArray();

            assets.Add(new HistoryAssetLibraryRecord(
                raw.AssetId,
                workspaceId,
                folderId,
                memberships,
                Math.Clamp(raw.UseCount, 0, MaximumUseCount),
                raw.LastUsedUtc,
                NormalizeActivityIds(raw.WorkflowIds, MaximumWorkflowIdsPerAsset),
                NormalizeActivityIds(raw.AiActionIds, MaximumAiActionIdsPerAsset)));
        }

        // Enforce per-collection member cap deterministically by newest activity then AssetId.
        var allowedByCollection = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var collection in collections)
        {
            allowedByCollection[collection.Id] = assets
                .Where(a => a.CollectionIds?.Contains(collection.Id, StringComparer.Ordinal) == true)
                .OrderByDescending(a => a.LastUsedUtc ?? DateTimeOffset.MinValue)
                .ThenBy(a => a.AssetId)
                .Take(MaximumCollectionMembers)
                .Select(a => a.AssetId)
                .ToHashSet();
        }
        assets = assets.Select(asset => asset with
        {
            CollectionIds = (asset.CollectionIds ?? []).Where(id => allowedByCollection.TryGetValue(id, out var allowed) && allowed.Contains(asset.AssetId)).ToArray()
        }).ToList();

        return new HistoryLibrarySnapshot(CurrentSchemaVersion, workspaces, folders, collections, assets);
    }

    public static IReadOnlyList<string> Validate(HistoryLibrarySnapshot? input)
    {
        var errors = new List<string>();
        if (input is null) return errors;
        if (input.SchemaVersion < 1 || input.SchemaVersion > CurrentSchemaVersion) errors.Add("Unsupported History library schema version.");
        if ((input.Workspaces?.Count ?? 0) > MaximumWorkspaces) errors.Add($"History library cannot exceed {MaximumWorkspaces} workspaces.");
        if ((input.Folders?.Count ?? 0) > MaximumFolders) errors.Add($"History library cannot exceed {MaximumFolders} folders.");
        if ((input.Collections?.Count ?? 0) > MaximumCollections) errors.Add($"History library cannot exceed {MaximumCollections} collections.");
        if ((input.Workspaces ?? []).Any(x => !IsSafeId(x.Id) || string.IsNullOrWhiteSpace(NormalizeName(x.Name)))) errors.Add("History workspace contains an invalid id or name.");
        if ((input.Folders ?? []).Any(x => !IsSafeId(x.Id) || !IsSafeId(x.WorkspaceId) || string.IsNullOrWhiteSpace(NormalizeName(x.Name)))) errors.Add("History folder contains an invalid id or name.");
        if ((input.Collections ?? []).Any(x => !IsSafeId(x.Id) || string.IsNullOrWhiteSpace(NormalizeName(x.Name)))) errors.Add("History collection contains an invalid id or name.");
        return errors;
    }

    public static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= MaximumNameLength ? normalized : normalized[..MaximumNameLength];
    }

    public static string? NormalizeActivityId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= MaximumActivityIdLength ? normalized : normalized[..MaximumActivityIdLength];
    }

    public static bool IsSafeId(string? value) =>
        value is { Length: 32 } && value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static IReadOnlyList<HistoryWorkspace> NormalizeWorkspaces(IReadOnlyList<HistoryWorkspace>? source)
    {
        var result = new List<HistoryWorkspace>();
        foreach (var raw in source ?? [])
        {
            var id = NormalizeId(raw.Id);
            var name = NormalizeName(raw.Name);
            if (id is null || name is null || result.Any(x => x.Id == id || string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new HistoryWorkspace(id, name, raw.CreatedUtc));
            if (result.Count >= MaximumWorkspaces) break;
        }
        return result;
    }

    private static IReadOnlyList<HistoryFolder> NormalizeFolders(IReadOnlyList<HistoryFolder>? source, HashSet<string> workspaceIds)
    {
        var result = new List<HistoryFolder>();
        foreach (var raw in source ?? [])
        {
            var id = NormalizeId(raw.Id);
            var workspaceId = NormalizeId(raw.WorkspaceId);
            var name = NormalizeName(raw.Name);
            if (id is null || workspaceId is null || name is null || !workspaceIds.Contains(workspaceId)) continue;
            if (result.Any(x => x.Id == id || (x.WorkspaceId == workspaceId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))) continue;
            if (result.Count(x => x.WorkspaceId == workspaceId) >= MaximumFoldersPerWorkspace) continue;
            result.Add(new HistoryFolder(id, workspaceId, name, raw.CreatedUtc));
            if (result.Count >= MaximumFolders) break;
        }
        return result;
    }

    private static IReadOnlyList<HistoryCollection> NormalizeCollections(IReadOnlyList<HistoryCollection>? source)
    {
        var result = new List<HistoryCollection>();
        foreach (var raw in source ?? [])
        {
            var id = NormalizeId(raw.Id);
            var name = NormalizeName(raw.Name);
            if (id is null || name is null || result.Any(x => x.Id == id || string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new HistoryCollection(id, name, raw.CreatedUtc));
            if (result.Count >= MaximumCollections) break;
        }
        return result;
    }

    private static string? NormalizeId(string? value) => IsSafeId(value) ? value : null;

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? values, int maximum) =>
        (values ?? []).Select(NormalizeId).Where(x => x is not null).Cast<string>().Distinct(StringComparer.Ordinal).Take(maximum).ToArray();

    private static IReadOnlyList<string> NormalizeActivityIds(IReadOnlyList<string>? values, int maximum) =>
        (values ?? []).Select(NormalizeActivityId).Where(x => x is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(maximum).ToArray();
}
