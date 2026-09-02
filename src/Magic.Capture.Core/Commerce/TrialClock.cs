namespace Magic.Capture.Core.Commerce;

public sealed record TrialEvaluation(
    ProductTier Tier,
    bool IsActive,
    DateTimeOffset EffectiveNowUtc,
    DateTimeOffset EndsUtc,
    TimeSpan Remaining,
    TrialState UpdatedState);

public static class TrialClock
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(168);

    public static TrialEvaluation Evaluate(TrialState state, DateTimeOffset systemNowUtc)
    {
        var normalizedNow = systemNowUtc.ToUniversalTime();
        var effectiveNow = normalizedNow > state.LastSeenUtc ? normalizedNow : state.LastSeenUtc;
        var ends = state.StartedUtc + Duration;
        var remaining = ends > effectiveNow ? ends - effectiveNow : TimeSpan.Zero;
        var active = effectiveNow < ends;
        var updated = state with { LastSeenUtc = effectiveNow };
        return new TrialEvaluation(active ? ProductTier.PlusTrial : ProductTier.Free, active, effectiveNow, ends, remaining, updated);
    }
}
