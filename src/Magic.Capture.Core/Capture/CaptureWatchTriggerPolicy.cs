namespace Magic.Capture.Core.Capture;

public readonly record struct CaptureWatchDecision(bool ShouldTrigger, bool EstablishBaseline);

public static class CaptureWatchTriggerPolicy
{
    public static CaptureWatchDecision Decide(
        bool onlyWhenChanged,
        bool hasBaseline,
        double changedPercent,
        double minimumChangedPercent)
    {
        var changed = double.IsFinite(changedPercent) ? Math.Clamp(changedPercent, 0, 100) : 0;
        var threshold = double.IsFinite(minimumChangedPercent) ? Math.Clamp(minimumChangedPercent, 0, 100) : 0;
        if (!onlyWhenChanged) return new CaptureWatchDecision(true, !hasBaseline);
        if (!hasBaseline) return new CaptureWatchDecision(false, true);
        return new CaptureWatchDecision(changed >= threshold, false);
    }
}
