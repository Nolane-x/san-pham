using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryTextIndexTests
{
    private static HistoryItem Item(Guid id, string title, string? ocr = null) =>
        new(id, DateTimeOffset.Parse("2026-08-24T00:00:00Z"), $"2026/08/24/{id:N}.png", 1280, 720,
            "Region", ocr, null, 1000, Title: title, ProcessName: "chrome.exe", WindowTitle: "Checkout — Chrome");

    [Fact]
    public void Search_intersects_terms_and_preserves_substring_candidates()
    {
        var one = Item(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Invoice payment", "Total 29.99 USD");
        var two = Item(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Invoice draft", "No total yet");
        var index = HistoryTextIndex.Build([one, two]);

        Assert.Equal([one.Id], index.Search("voice 29.99"));
        Assert.Equal([one.Id, two.Id], index.Search("invoice").OrderBy(id => id));
    }

    [Fact]
    public void Empty_query_returns_every_indexed_id()
    {
        var one = Item(Guid.NewGuid(), "Alpha");
        var two = Item(Guid.NewGuid(), "Beta");
        var index = HistoryTextIndex.Build([one, two]);

        Assert.Equal(2, index.Search("   ").Count);
    }
}
