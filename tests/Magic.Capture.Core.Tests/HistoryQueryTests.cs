using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryQueryTests
{
    private static HistoryItem Item(
        string id,
        DateTimeOffset created,
        int width = 1280,
        int height = 720,
        string source = "Region",
        string? ocr = null,
        string? barcode = null,
        long bytes = 100,
        bool favorite = false,
        string? session = null,
        string? sourceDisplay = null,
        string? window = null,
        string? process = null,
        string? monitor = null) =>
        new(Guid.Parse(id), created, $"{id}.png", width, height, source, ocr, barcode, bytes, null,
            IsFavorite: favorite, SessionId: session, SourceDisplayName: sourceDisplay, WindowTitle: window, ProcessName: process, MonitorName: monitor);

    [Fact]
    public void Apply_filters_metadata_without_touching_pixels()
    {
        var first = Item("11111111-1111-1111-1111-111111111111", DateTimeOffset.Parse("2026-08-20T12:00:00Z"),
            1920, 1080, "Region", "invoice total", "qr-42", 900, true, "run-a", "Chrome", "Checkout", "chrome.exe");
        var second = Item("22222222-2222-2222-2222-222222222222", DateTimeOffset.Parse("2026-08-22T12:00:00Z"),
            800, 600, "Window", null, null, 200, false, "run-b", "Notepad", "Notes", "notepad.exe");

        var result = HistoryQuery.Apply([first, second], new HistoryQueryOptions(
            FromUtc: DateTimeOffset.Parse("2026-08-19T00:00:00Z"),
            ToUtc: DateTimeOffset.Parse("2026-08-21T23:59:59Z"),
            SourceOrAppContains: "chrome",
            WindowContains: "check",
            CaptureType: "Region",
            MinWidth: 1900,
            MaxWidth: 2000,
            MinHeight: 1000,
            MaxHeight: 1200,
            HasOcr: true,
            HasBarcode: true,
            IsFavorite: true,
            SessionId: "run-a"));

        Assert.Single(result);
        Assert.Equal(first.Id, result[0].Id);
    }

    [Fact]
    public void Apply_sorts_newest_oldest_and_file_size()
    {
        var a = Item("11111111-1111-1111-1111-111111111111", DateTimeOffset.Parse("2026-08-20T00:00:00Z"), bytes: 300);
        var b = Item("22222222-2222-2222-2222-222222222222", DateTimeOffset.Parse("2026-08-22T00:00:00Z"), bytes: 100);
        var c = Item("33333333-3333-3333-3333-333333333333", DateTimeOffset.Parse("2026-08-21T00:00:00Z"), bytes: 900);

        Assert.Equal([b.Id, c.Id, a.Id], HistoryQuery.Apply([a, b, c], new(Sort: HistorySortOrder.Newest)).Select(x => x.Id));
        Assert.Equal([a.Id, c.Id, b.Id], HistoryQuery.Apply([a, b, c], new(Sort: HistorySortOrder.Oldest)).Select(x => x.Id));
        Assert.Equal([c.Id, a.Id, b.Id], HistoryQuery.Apply([a, b, c], new(Sort: HistorySortOrder.FileSizeDescending)).Select(x => x.Id));
        Assert.Equal([b.Id, a.Id, c.Id], HistoryQuery.Apply([a, b, c], new(Sort: HistorySortOrder.FileSizeAscending)).Select(x => x.Id));
    }

    [Fact]
    public void Apply_filters_monitor_metadata()
    {
        var left = Item("11111111-1111-1111-1111-111111111111", DateTimeOffset.Parse("2026-08-20T00:00:00Z"), monitor: @"\\.\DISPLAY1");
        var right = Item("22222222-2222-2222-2222-222222222222", DateTimeOffset.Parse("2026-08-20T00:00:00Z"), monitor: @"\\.\DISPLAY2");
        var result = HistoryQuery.Apply([left, right], new HistoryQueryOptions(MonitorContains: "DISPLAY2"));
        Assert.Single(result);
        Assert.Equal(right.Id, result[0].Id);
    }

    [Fact]
    public void Apply_normalizes_invalid_bounds_to_no_constraint()
    {
        var item = Item("11111111-1111-1111-1111-111111111111", DateTimeOffset.Parse("2026-08-20T00:00:00Z"), 640, 480);
        var result = HistoryQuery.Apply([item], new HistoryQueryOptions(MinWidth: -4, MaxHeight: 0));
        Assert.Single(result);
    }
}
