using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryLibraryTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Normalize_prunes_invalid_links_and_bounds_activity()
    {
        var workspace = new HistoryWorkspace("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", " Work ", DateTimeOffset.UnixEpoch);
        var folder = new HistoryFolder("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", workspace.Id, "Shots", DateTimeOffset.UnixEpoch);
        var collection = new HistoryCollection("cccccccccccccccccccccccccccccccc", " Picks ", DateTimeOffset.UnixEpoch);
        var record = new HistoryAssetLibraryRecord(A, workspace.Id, folder.Id,
            [collection.Id, collection.Id, "missing"], 2_000_000, DateTimeOffset.UtcNow,
            Enumerable.Range(0, 40).Select(i => "wf-" + i).ToArray(),
            Enumerable.Range(0, 40).Select(i => "ai-" + i).ToArray());
        var normalized = HistoryLibraryPolicy.Normalize(new HistoryLibrarySnapshot(1, [workspace], [folder], [collection], [record]));
        var asset = Assert.Single(normalized.Assets);
        Assert.Equal(HistoryLibraryPolicy.MaximumUseCount, asset.UseCount);
        Assert.Single(asset.CollectionIds!);
        Assert.Equal(HistoryLibraryPolicy.MaximumWorkflowIdsPerAsset, asset.WorkflowIds!.Count);
        Assert.Equal(HistoryLibraryPolicy.MaximumAiActionIdsPerAsset, asset.AiActionIds!.Count);
    }

    [Fact]
    public void Query_filters_library_metadata_and_sorts_most_used()
    {
        var first = new HistoryItem(A, DateTimeOffset.Parse("2026-08-20T00:00:00Z"), "a.png", 100, 100, "Region", null, null, 100);
        var second = new HistoryItem(B, DateTimeOffset.Parse("2026-08-21T00:00:00Z"), "b.png", 100, 100, "Region", null, null, 100);
        var ws = new HistoryWorkspace("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Work", DateTimeOffset.UnixEpoch);
        var folder = new HistoryFolder("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", ws.Id, "Shots", DateTimeOffset.UnixEpoch);
        var col = new HistoryCollection("cccccccccccccccccccccccccccccccc", "Picks", DateTimeOffset.UnixEpoch);
        var lib = new HistoryLibrarySnapshot(1,[ws],[folder],[col],
        [
            new HistoryAssetLibraryRecord(A, ws.Id, folder.Id, [col.Id], 2, DateTimeOffset.UtcNow, ["workflow.a"], ["magic.a"]),
            new HistoryAssetLibraryRecord(B, ws.Id, folder.Id, [col.Id], 9, DateTimeOffset.UtcNow, ["workflow.a"], ["magic.a"])
        ]);
        var result = HistoryQuery.Apply([first,second], new HistoryQueryOptions(
            WorkspaceId: ws.Id, FolderId: folder.Id, CollectionId: col.Id, WorkflowId: "workflow.a", AiActionId: "magic.a", Sort: HistorySortOrder.MostUsed), lib);
        Assert.Equal([B,A], result.Select(x => x.Id));
    }
}
