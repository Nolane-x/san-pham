using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryDuplicateIndexTests
{
    private static HistoryItem Item(Guid id, string? sha, ulong? dhash) =>
        new(id, DateTimeOffset.UtcNow, $"{id:N}.png", 10, 10, "Region", null, null, 10,
            ContentSha256: sha, PerceptualHash64: dhash);

    [Fact]
    public void Exact_groups_only_valid_sha256_duplicates()
    {
        var sha = new string('a', 64);
        var a = Item(Guid.NewGuid(), sha, 0);
        var b = Item(Guid.NewGuid(), sha.ToUpperInvariant(), 0);
        var c = Item(Guid.NewGuid(), "not-a-hash", 0);

        var groups = HistoryDuplicateIndex.FindExact([a, b, c]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public void Near_groups_hashes_within_hamming_threshold()
    {
        var a = Item(Guid.NewGuid(), null, 0UL);
        var b = Item(Guid.NewGuid(), null, 0b111UL);
        var c = Item(Guid.NewGuid(), null, ulong.MaxValue);

        var groups = HistoryDuplicateIndex.FindNear([a, b, c], 3);

        var group = Assert.Single(groups);
        Assert.Contains(group.Items, item => item.Id == a.Id);
        Assert.Contains(group.Items, item => item.Id == b.Id);
        Assert.DoesNotContain(group.Items, item => item.Id == c.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void Near_rejects_threshold_outside_pigeonhole_bound(int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HistoryDuplicateIndex.FindNear([], threshold));
    }
}
