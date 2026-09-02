using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Capture;

internal sealed record CaptureBackendAttempt(
    CaptureBackendKind Backend,
    bool Succeeded,
    TimeSpan Duration,
    CaptureBackendFailureKind? FailureKind = null,
    string? Message = null,
    int RecoveryCount = 0,
    bool Skipped = false);

internal sealed record CaptureAttemptDiagnostics(
    string Backend,
    int Attempts,
    PixelRect Bounds,
    IReadOnlyList<string> Failures,
    IReadOnlyList<CaptureBackendAttempt>? BackendAttempts = null)
{
    public bool RecoveredAfterRetry => Attempts > 1 && Failures.Count > 0;
    public bool UsedFallback => BackendAttempts is { Count: > 1 } attempts && attempts.Take(attempts.Count - 1).Any(item => !item.Succeeded);
}

internal sealed record CaptureWithDiagnosticsResult(CaptureAsset Asset, CaptureAttemptDiagnostics Diagnostics);
