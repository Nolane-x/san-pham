using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryStoragePathPolicyTests
{
    [Fact]
    public void Accepts_only_id_named_primary_and_thumbnail_files()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Assert.True(HistoryStoragePathPolicy.IsExpectedPrimary(id, "2026/08/24/00112233445566778899aabbccddeeff.png"));
        Assert.True(HistoryStoragePathPolicy.IsExpectedThumbnail(id, @"2026\08\24\00112233445566778899aabbccddeeff.thumb.png"));
    }

    [Theory]
    [InlineData("history-index.json")]
    [InlineData("settings.json")]
    [InlineData("2026/08/24/other.png")]
    [InlineData("2026/08/24/00112233445566778899aabbccddeeff.thumb.png")]
    public void Rejects_primary_paths_that_do_not_match_capture_id(string relativePath)
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Assert.False(HistoryStoragePathPolicy.IsExpectedPrimary(id, relativePath));
    }

    [Fact]
    public void Rejects_thumbnail_path_that_points_at_primary()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Assert.False(HistoryStoragePathPolicy.IsExpectedThumbnail(id, "00112233445566778899aabbccddeeff.png"));
    }
}
