namespace Magic.Capture.Core.History;

public sealed record HistorySessionSummary(
    string SessionId,
    DateTimeOffset FirstCaptureUtc,
    DateTimeOffset LastCaptureUtc,
    int CaptureCount,
    long TotalBytes,
    IReadOnlyList<string> ProcessNames,
    IReadOnlyList<string> SourceKinds);

public static class HistorySessions
{
    public const string LegacySessionId = "legacy-unassigned";

    public static IReadOnlyList<HistorySessionSummary> Summarize(IEnumerable<HistoryItem>? items)
    {
        return (items ?? [])
            .Where(item => item is not null)
            .GroupBy(item => NormalizeSession(item.SessionId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.CreatedUtc).ToArray();
                return new HistorySessionSummary(
                    group.Key,
                    ordered[0].CreatedUtc,
                    ordered[^1].CreatedUtc,
                    ordered.Length,
                    ordered.Aggregate(0L, (total, item) => SaturatingAdd(total, Math.Max(0, item.FileBytes))),
                    ordered.Select(item => item.ProcessName).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(64).ToArray(),
                    ordered.Select(item => item.SourceKind).Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(64).ToArray());
            })
            .OrderByDescending(summary => summary.LastCaptureUtc)
            .ThenBy(summary => summary.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeSession(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? LegacySessionId : sessionId.Trim();

    private static long SaturatingAdd(long left, long right) =>
        long.MaxValue - left < right ? long.MaxValue : left + right;
}
