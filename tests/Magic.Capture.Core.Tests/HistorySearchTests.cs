using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistorySearchTests
{
    private static readonly HistoryItem Item = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new DateTimeOffset(2026, 8, 23, 12, 30, 0, TimeSpan.Zero),
        @"2026\08\23\capture.png",
        1280,
        720,
        "Region",
        "Invoice ACME total 29.99 USD",
        "https://example.com/order/42",
        1024,
        @"2026\08\23\capture.thumb.png");

    [Theory]
    [InlineData("")]
    [InlineData("invoice")]
    [InlineData("ACME")]
    [InlineData("example.com")]
    [InlineData("region")]
    [InlineData("1280x720")]
    [InlineData("1280 × 720")]
    [InlineData("capture.png")]
    [InlineData("2026-08-23")]
    public void Search_matches_useful_history_fields(string query)
    {
        Assert.True(HistorySearch.Matches(Item, query));
    }

    [Fact]
    public void Search_is_case_insensitive_and_trims_query()
    {
        Assert.True(HistorySearch.Matches(Item, "   usd  "));
    }

    [Fact]
    public void Search_rejects_unrelated_query()
    {
        Assert.False(HistorySearch.Matches(Item, "photoshop"));
    }

    [Fact]
    public void Search_all_requires_every_term_to_match_some_field()
    {
        Assert.True(HistorySearch.Matches(Item, "invoice 29.99"));
        Assert.True(HistorySearch.Matches(Item, "region 1280"));
        Assert.False(HistorySearch.Matches(Item, "invoice photoshop"));
    }
}

public sealed class HistorySearchMetadataTests
{
    [Fact]
    public void Search_matches_title_notes_tags_and_process_metadata()
    {
        var item = new HistoryItem(Guid.NewGuid(), DateTimeOffset.Parse("2026-08-24T00:00:00Z"), "a.png", 800, 600, "Region", null, null, 123, null,
            Title: "Login failure", Notes: "Reproduce on checkout", Tags: ["bug", "payment"], IsFavorite: true,
            SessionId: "session-1", SourceDisplayName: "Checkout", WindowTitle: "Payment — Chrome", ProcessName: "chrome.exe");
        Assert.True(HistorySearch.Matches(item, "login payment chrome"));
        Assert.True(HistorySearch.Matches(item, "checkout bug"));
        Assert.False(HistorySearch.Matches(item, "firefox"));
    }
}
