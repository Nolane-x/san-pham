namespace Magic.Capture.Core.History;

public sealed record HistoryRetentionPolicy(int? MaximumAgeDays, int? MaximumCount, long? MaximumBytes)
{
    public static HistoryRetentionPolicy Default => new(30, 500, null);
}
