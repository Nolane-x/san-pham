using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistorySessionsTests
{
    [Fact]
    public void Summarize_groups_sessions_and_orders_newest_first()
    {
        var items = new[]
        {
            new HistoryItem(Guid.NewGuid(), DateTimeOffset.Parse("2026-08-24T01:00:00Z"), "a.png", 10, 10, "Region", null, null, 10, SessionId: "run-a", ProcessName: "a.exe"),
            new HistoryItem(Guid.NewGuid(), DateTimeOffset.Parse("2026-08-24T02:00:00Z"), "b.png", 10, 10, "Region", null, null, 20, SessionId: "run-a", ProcessName: "b.exe"),
            new HistoryItem(Guid.NewGuid(), DateTimeOffset.Parse("2026-08-24T03:00:00Z"), "c.png", 10, 10, "Region", null, null, 30, SessionId: "run-b", ProcessName: "c.exe")
        };

        var summaries = HistorySessions.Summarize(items);

        Assert.Equal("run-b", summaries[0].SessionId);
        Assert.Equal(1, summaries[0].CaptureCount);
        Assert.Equal("run-a", summaries[1].SessionId);
        Assert.Equal(2, summaries[1].CaptureCount);
        Assert.Equal(30, summaries[1].TotalBytes);
        Assert.Equal(2, summaries[1].ProcessNames.Count);
    }
}
