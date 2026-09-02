using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryMetadataTests
{
    [Fact]
    public void Normalize_trims_deduplicates_and_bounds_tags()
    {
        var update = HistoryMetadata.Normalize("  Login error  ", "  note  ", [" bug ", "BUG", " ui ", "", "  "]);
        Assert.Equal("Login error", update.Title);
        Assert.Equal("note", update.Notes);
        Assert.Equal(["bug", "ui"], update.Tags);
    }

    [Fact]
    public void Normalize_caps_metadata_to_safe_sizes()
    {
        var update = HistoryMetadata.Normalize(new string('t', 400), new string('n', 9000), Enumerable.Range(0, 100).Select(i => $"tag{i}").ToArray());
        Assert.Equal(240, update.Title!.Length);
        Assert.Equal(4096, update.Notes!.Length);
        Assert.Equal(32, update.Tags.Count);
    }
    [Fact]
    public void NormalizeSessionId_trims_and_bounds_values()
    {
        Assert.Equal("session-a", HistoryMetadata.NormalizeSessionId("  session-a  "));
        Assert.Equal(HistoryMetadata.MaxSessionIdLength, HistoryMetadata.NormalizeSessionId(new string('s', 300))!.Length);
        Assert.Null(HistoryMetadata.NormalizeSessionId("   "));
    }

}
