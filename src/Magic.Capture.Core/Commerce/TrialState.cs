namespace Magic.Capture.Core.Commerce;

public sealed record TrialState
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
    public bool ExpiryNoticeShown { get; init; }

    public static TrialState Create(DateTimeOffset nowUtc) => new()
    {
        StartedUtc = nowUtc.ToUniversalTime(),
        LastSeenUtc = nowUtc.ToUniversalTime(),
        ExpiryNoticeShown = false
    };
}
