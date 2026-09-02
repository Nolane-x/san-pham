using Magic.Capture.Core.History;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.App.Persistence;

internal sealed class HistoryLibraryStore
{
    public const long MaximumLibraryJsonBytes = 32L * 1024 * 1024;
    private readonly AppPaths _paths;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryLibraryStore(AppPaths paths, LocalLog log)
    {
        _paths = paths;
        _log = log;
    }

    public async Task<HistoryLibrarySnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try { return await LoadUnsafeAsync(cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error("HistoryLibraryReadTransient", ex);
                return HistoryLibrarySnapshot.Empty;
            }
        }
        finally { _gate.Release(); }
    }

    public Task<HistoryWorkspace> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default) =>
        MutateAsync(snapshot =>
        {
            var normalizedName = RequireName(name);
            if (snapshot.Workspaces.Count >= HistoryLibraryPolicy.MaximumWorkspaces) throw new InvalidOperationException("History workspace limit reached.");
            if (snapshot.Workspaces.Any(x => string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A workspace with that name already exists.");
            var created = new HistoryWorkspace(NewId(), normalizedName, DateTimeOffset.UtcNow);
            return (snapshot with { Workspaces = snapshot.Workspaces.Append(created).ToArray() }, created);
        }, cancellationToken);

    public Task<HistoryFolder> CreateFolderAsync(string workspaceId, string name, CancellationToken cancellationToken = default) =>
        MutateAsync(snapshot =>
        {
            var workspace = snapshot.Workspaces.FirstOrDefault(x => x.Id == workspaceId) ?? throw new InvalidOperationException("Workspace not found.");
            var normalizedName = RequireName(name);
            if (snapshot.Folders.Count >= HistoryLibraryPolicy.MaximumFolders || snapshot.Folders.Count(x => x.WorkspaceId == workspace.Id) >= HistoryLibraryPolicy.MaximumFoldersPerWorkspace)
                throw new InvalidOperationException("History folder limit reached.");
            if (snapshot.Folders.Any(x => x.WorkspaceId == workspace.Id && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A folder with that name already exists in this workspace.");
            var created = new HistoryFolder(NewId(), workspace.Id, normalizedName, DateTimeOffset.UtcNow);
            return (snapshot with { Folders = snapshot.Folders.Append(created).ToArray() }, created);
        }, cancellationToken);

    public Task<HistoryCollection> CreateCollectionAsync(string name, CancellationToken cancellationToken = default) =>
        MutateAsync(snapshot =>
        {
            var normalizedName = RequireName(name);
            if (snapshot.Collections.Count >= HistoryLibraryPolicy.MaximumCollections) throw new InvalidOperationException("History collection limit reached.");
            if (snapshot.Collections.Any(x => string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A collection with that name already exists.");
            var created = new HistoryCollection(NewId(), normalizedName, DateTimeOffset.UtcNow);
            return (snapshot with { Collections = snapshot.Collections.Append(created).ToArray() }, created);
        }, cancellationToken);

    public Task RenameWorkspaceAsync(string id, string name, CancellationToken cancellationToken = default) => MutateUnitAsync(snapshot =>
    {
        var normalizedName = RequireName(name);
        if (snapshot.Workspaces.Any(x => x.Id != id && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A workspace with that name already exists.");
        var found = false;
        var values = snapshot.Workspaces.Select(x => { if (x.Id != id) return x; found = true; return x with { Name = normalizedName }; }).ToArray();
        if (!found) throw new InvalidOperationException("Workspace not found.");
        return snapshot with { Workspaces = values };
    }, cancellationToken);

    public Task RenameFolderAsync(string id, string name, CancellationToken cancellationToken = default) => MutateUnitAsync(snapshot =>
    {
        var target = snapshot.Folders.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Folder not found.");
        var normalizedName = RequireName(name);
        if (snapshot.Folders.Any(x => x.Id != id && x.WorkspaceId == target.WorkspaceId && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A folder with that name already exists in this workspace.");
        return snapshot with { Folders = snapshot.Folders.Select(x => x.Id == id ? x with { Name = normalizedName } : x).ToArray() };
    }, cancellationToken);

    public Task RenameCollectionAsync(string id, string name, CancellationToken cancellationToken = default) => MutateUnitAsync(snapshot =>
    {
        var normalizedName = RequireName(name);
        if (!snapshot.Collections.Any(x => x.Id == id)) throw new InvalidOperationException("Collection not found.");
        if (snapshot.Collections.Any(x => x.Id != id && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A collection with that name already exists.");
        return snapshot with { Collections = snapshot.Collections.Select(x => x.Id == id ? x with { Name = normalizedName } : x).ToArray() };
    }, cancellationToken);

    public Task DeleteWorkspaceAsync(string id, CancellationToken cancellationToken = default) => MutateUnitAsync(snapshot =>
    {
        var folderIds = snapshot.Folders.Where(x => x.WorkspaceId == id).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return snapshot with
        {
            Workspaces = snapshot.Workspaces.Where(x => x.Id != id).ToArray(),
            Folders = snapshot.Folders.Where(x => x.WorkspaceId != id).ToArray(),
            Assets = snapshot.Assets.Select(x => x.WorkspaceId == id || (x.FolderId is not null && folderIds.Contains(x.FolderId)) ? x with { WorkspaceId = null, FolderId = null } : x).ToArray()
        };
    }, cancellationToken);

    public Task DeleteFolderAsync(string id, CancellationToken cancellationToken = default) => MutateUnitAsync(snapshot => snapshot with
    {
        Folders = snapshot.Folders.Where(x => x.Id != id).ToArray(),
        Assets = snapshot.Assets.Select(x => x.FolderId == id ? x with { FolderId = null } : x).ToArray()
    }, cancellationToken);

    public Task DeleteCollectionAsync(string id, CancellationToken cancellationToken = default) => MutateUnitAsync(snapshot => snapshot with
    {
        Collections = snapshot.Collections.Where(x => x.Id != id).ToArray(),
        Assets = snapshot.Assets.Select(x => x with { CollectionIds = (x.CollectionIds ?? []).Where(value => value != id).ToArray() }).ToArray()
    }, cancellationToken);

    public Task AssignWorkspaceFolderAsync(IEnumerable<Guid> assetIds, string? workspaceId, string? folderId, CancellationToken cancellationToken = default) =>
        MutateAssetsAsync(assetIds, (snapshot, record) =>
        {
            if (workspaceId is not null && !snapshot.Workspaces.Any(x => x.Id == workspaceId)) throw new InvalidOperationException("Workspace not found.");
            if (folderId is not null)
            {
                var folder = snapshot.Folders.FirstOrDefault(x => x.Id == folderId) ?? throw new InvalidOperationException("Folder not found.");
                if (workspaceId is null || folder.WorkspaceId != workspaceId) throw new InvalidOperationException("Folder does not belong to the selected workspace.");
            }
            return record with { WorkspaceId = workspaceId, FolderId = folderId };
        }, cancellationToken);

    public Task SetCollectionMembershipAsync(IEnumerable<Guid> assetIds, string collectionId, bool isMember, CancellationToken cancellationToken = default)
    {
        var targetIds = assetIds.Where(id => id != Guid.Empty).Distinct().Take(5_000).ToArray();
        if (targetIds.Length == 0) return Task.CompletedTask;
        return MutateUnitAsync(snapshot =>
        {
            if (!snapshot.Collections.Any(x => x.Id == collectionId)) throw new InvalidOperationException("Collection not found.");
            var byId = snapshot.Assets.ToDictionary(x => x.AssetId);
            if (isMember)
            {
                var existingMembers = snapshot.Assets.Count(record => record.CollectionIds?.Contains(collectionId, StringComparer.Ordinal) == true);
                var newMembers = targetIds.Count(id => !byId.TryGetValue(id, out var existing) || existing.CollectionIds?.Contains(collectionId, StringComparer.Ordinal) != true);
                if (existingMembers + newMembers > HistoryLibraryPolicy.MaximumCollectionMembers)
                    throw new InvalidOperationException("Collection member limit reached.");
            }
            foreach (var assetId in targetIds)
            {
                var record = byId.TryGetValue(assetId, out var existing) ? existing : new HistoryAssetLibraryRecord(assetId);
                var ids = (record.CollectionIds ?? []).Where(id => !string.Equals(id, collectionId, StringComparison.Ordinal)).ToList();
                if (isMember)
                {
                    if (ids.Count >= HistoryLibraryPolicy.MaximumCollectionsPerAsset) throw new InvalidOperationException("A capture cannot belong to more collections.");
                    ids.Add(collectionId);
                }
                byId[assetId] = record with { CollectionIds = ids };
            }
            return snapshot with { Assets = byId.Values.OrderBy(record => record.AssetId).ToArray() };
        }, cancellationToken);
    }

    public Task RecordOpenedAsync(Guid assetId, CancellationToken cancellationToken = default) => RecordActivityAsync(assetId, null, null, incrementUse: true, cancellationToken);
    public Task RecordWorkflowAsync(Guid assetId, string workflowId, CancellationToken cancellationToken = default) => RecordActivityAsync(assetId, workflowId, null, incrementUse: true, cancellationToken);
    public Task RecordAiActionAsync(Guid assetId, string aiActionId, CancellationToken cancellationToken = default) => RecordActivityAsync(assetId, null, aiActionId, incrementUse: false, cancellationToken);

    public async Task PruneAssetsBestEffortAsync(IEnumerable<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        var ids = assetIds.Where(x => x != Guid.Empty).Distinct().Take(10_000).ToHashSet();
        if (ids.Count == 0) return;
        try
        {
            await MutateUnitAsync(snapshot => snapshot with { Assets = snapshot.Assets.Where(x => !ids.Contains(x.AssetId)).ToArray() }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { _log.Error("HistoryLibraryPrune", ex); }
    }

    private Task RecordActivityAsync(Guid assetId, string? workflowId, string? aiActionId, bool incrementUse, CancellationToken cancellationToken) =>
        MutateAssetsAsync([assetId], (_, record) =>
        {
            var workflows = MergeActivity(record.WorkflowIds, workflowId, HistoryLibraryPolicy.MaximumWorkflowIdsPerAsset);
            var actions = MergeActivity(record.AiActionIds, aiActionId, HistoryLibraryPolicy.MaximumAiActionIdsPerAsset);
            return record with
            {
                UseCount = incrementUse ? Math.Min(HistoryLibraryPolicy.MaximumUseCount, record.UseCount + 1) : record.UseCount,
                LastUsedUtc = DateTimeOffset.UtcNow,
                WorkflowIds = workflows,
                AiActionIds = actions
            };
        }, cancellationToken);

    private async Task MutateAssetsAsync(IEnumerable<Guid> assetIds, Func<HistoryLibrarySnapshot, HistoryAssetLibraryRecord, HistoryAssetLibraryRecord> mutation, CancellationToken cancellationToken)
    {
        var ids = assetIds.Where(x => x != Guid.Empty).Distinct().Take(5_000).ToHashSet();
        if (ids.Count == 0) return;
        await MutateUnitAsync(snapshot =>
        {
            var byId = snapshot.Assets.ToDictionary(x => x.AssetId);
            foreach (var id in ids)
            {
                var record = byId.TryGetValue(id, out var existing) ? existing : new HistoryAssetLibraryRecord(id);
                byId[id] = mutation(snapshot, record);
            }
            return snapshot with { Assets = byId.Values.OrderBy(x => x.AssetId).ToArray() };
        }, cancellationToken);
    }

    private async Task<T> MutateAsync<T>(Func<HistoryLibrarySnapshot, (HistoryLibrarySnapshot Snapshot, T Result)> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnsafeAsync(cancellationToken);
            var (next, result) = mutation(current);
            await SaveUnsafeAsync(next, cancellationToken);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task MutateUnitAsync(Func<HistoryLibrarySnapshot, HistoryLibrarySnapshot> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnsafeAsync(cancellationToken);
            await SaveUnsafeAsync(mutation(current), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<HistoryLibrarySnapshot> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.HistoryLibraryFile) && !File.Exists(_paths.HistoryLibraryFile + ".bak")) return HistoryLibrarySnapshot.Empty;
        try
        {
            var loaded = await AtomicJsonFile.ReadAsync<HistoryLibrarySnapshot>(_paths.HistoryLibraryFile, cancellationToken, MaximumLibraryJsonBytes) ?? HistoryLibrarySnapshot.Empty;
            var errors = HistoryLibraryPolicy.Validate(loaded);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            return HistoryLibraryPolicy.Normalize(loaded);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException ex)
        {
            QuarantineLibrary();
            _log.Error("HistoryLibraryLoadCorrupt", ex);
            return HistoryLibrarySnapshot.Empty;
        }
    }

    private async Task SaveUnsafeAsync(HistoryLibrarySnapshot snapshot, CancellationToken cancellationToken)
    {
        var normalized = HistoryLibraryPolicy.Normalize(snapshot);
        await AtomicJsonFile.WriteAsync(_paths.HistoryLibraryFile, normalized, cancellationToken, MaximumLibraryJsonBytes);
    }

    private void QuarantineLibrary()
    {
        foreach (var path in new[] { _paths.HistoryLibraryFile, _paths.HistoryLibraryFile + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try { File.Move(path, path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}", false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string RequireName(string value) => HistoryLibraryPolicy.NormalizeName(value) ?? throw new InvalidOperationException("Name is required.");
    private static string NewId() => Guid.NewGuid().ToString("N");
    private static IReadOnlyList<string> MergeActivity(IReadOnlyList<string>? existing, string? incoming, int maximum)
    {
        var normalized = HistoryLibraryPolicy.NormalizeActivityId(incoming);
        var list = (existing ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalized is not null)
        {
            list.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, normalized);
        }
        return list.Take(maximum).ToArray();
    }
}
