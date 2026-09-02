using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryRetentionPlannerTests
{
    [Fact]
    public void DeletesItemsOlderThanAgeLimit()
    {
        var now = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        var old = Item(now.AddDays(-31), 100);
        var recent = Item(now.AddDays(-2), 100);

        var deleted = HistoryRetentionPlanner.SelectForDeletion([old, recent], new HistoryRetentionPolicy(30, null, null), now);

        Assert.Contains(old.Id, deleted);
        Assert.DoesNotContain(recent.Id, deleted);
    }

    [Fact]
    public void KeepsNewestItemsWithinCount()
    {
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(0, 5).Select(i => Item(now.AddMinutes(-i), 100)).ToArray();
        var deleted = HistoryRetentionPlanner.SelectForDeletion(items, new HistoryRetentionPolicy(null, 2, null), now);
        Assert.Equal(3, deleted.Count);
        Assert.DoesNotContain(items[0].Id, deleted);
        Assert.DoesNotContain(items[1].Id, deleted);
    }

    [Fact]
    public void AppliesStorageBudgetAfterOtherRules()
    {
        var now = DateTimeOffset.UtcNow;
        var items = new[] { Item(now, 600), Item(now.AddMinutes(-1), 500), Item(now.AddMinutes(-2), 400) };
        var deleted = HistoryRetentionPlanner.SelectForDeletion(items, new HistoryRetentionPolicy(null, null, 1_000), now);
        Assert.Contains(items[2].Id, deleted);
        Assert.DoesNotContain(items[0].Id, deleted);
    }

    private static HistoryItem Item(DateTimeOffset created, long bytes) =>
        new(Guid.NewGuid(), created, "x.png", 100, 100, "Region", null, null, bytes);
}
