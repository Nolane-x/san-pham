namespace Magic.Capture.Core.Capture;

public enum CaptureBackendKind
{
    WindowsGraphicsCapture,
    DesktopDuplication,
    Gdi
}

public enum CaptureTargetKind
{
    Window,
    Monitor,
    RegionSingleMonitor,
    RegionCrossMonitor,
    VirtualDesktop
}

public enum CaptureBackendPreference
{
    Auto,
    WindowsGraphicsCapture,
    DesktopDuplication,
    Gdi
}

public sealed record CaptureBackendAvailability(
    bool WindowsGraphicsCapture,
    bool DesktopDuplication,
    bool Gdi)
{
    public bool IsAvailable(CaptureBackendKind backend) => backend switch
    {
        CaptureBackendKind.WindowsGraphicsCapture => WindowsGraphicsCapture,
        CaptureBackendKind.DesktopDuplication => DesktopDuplication,
        CaptureBackendKind.Gdi => Gdi,
        _ => false
    };
}

public enum CaptureBackendFailureKind
{
    Timeout,
    AccessLost,
    DeviceRemoved,
    DeviceReset,
    AccessDenied,
    Unsupported,
    InvalidFrame,
    Permanent,
    Cancelled
}

public static class CaptureBackendPolicy
{
    public static IReadOnlyList<CaptureBackendKind> BuildCandidates(
        CaptureTargetKind target,
        bool includeCursor,
        CaptureBackendAvailability availability,
        CaptureBackendPreference preference = CaptureBackendPreference.Auto)
    {
        ArgumentNullException.ThrowIfNull(availability);

        var ordered = target switch
        {
            CaptureTargetKind.Window => new[]
            {
                CaptureBackendKind.WindowsGraphicsCapture,
                CaptureBackendKind.Gdi
            },
            CaptureTargetKind.Monitor or CaptureTargetKind.RegionSingleMonitor => includeCursor
                ? new[]
                {
                    CaptureBackendKind.WindowsGraphicsCapture,
                    CaptureBackendKind.Gdi
                }
                : new[]
                {
                    CaptureBackendKind.WindowsGraphicsCapture,
                    CaptureBackendKind.DesktopDuplication,
                    CaptureBackendKind.Gdi
                },
            CaptureTargetKind.RegionCrossMonitor or CaptureTargetKind.VirtualDesktop => new[]
            {
                CaptureBackendKind.Gdi
            },
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        var candidates = ordered
            .Where(availability.IsAvailable)
            .Distinct()
            .ToList();

        var preferred = preference switch
        {
            CaptureBackendPreference.WindowsGraphicsCapture => CaptureBackendKind.WindowsGraphicsCapture,
            CaptureBackendPreference.DesktopDuplication => CaptureBackendKind.DesktopDuplication,
            CaptureBackendPreference.Gdi => CaptureBackendKind.Gdi,
            _ => (CaptureBackendKind?)null
        };

        if (preferred is { } preferredBackend)
        {
            var index = candidates.IndexOf(preferredBackend);
            if (index > 0)
            {
                candidates.RemoveAt(index);
                candidates.Insert(0, preferredBackend);
            }
        }

        return candidates;
    }
}

public static class DesktopDuplicationCursorPolicy
{
    /// <summary>
    /// Desktop Duplication can only prove cursor exclusion when the frame reports a current,
    /// separately overlaid pointer. If the pointer is not reported separately, DXGI permits the
    /// pointer to be embedded in the desktop image; fail closed and let the router use GDI.
    /// </summary>
    public static bool CanGuaranteeCursorExcluded(long lastMouseUpdateTime, bool separatePointerVisible) =>
        lastMouseUpdateTime != 0 && separatePointerVisible;
}

public static class CaptureBackendRecoveryPolicy
{
    public const int DesktopDuplicationRebuildBudget = 1;

    public static bool ShouldRebuildDesktopDuplication(CaptureBackendFailureKind failure, int rebuildsUsed) =>
        rebuildsUsed >= 0 &&
        rebuildsUsed < DesktopDuplicationRebuildBudget &&
        failure is CaptureBackendFailureKind.AccessLost or CaptureBackendFailureKind.DeviceRemoved or CaptureBackendFailureKind.DeviceReset;

    public static bool ShouldFallback(CaptureBackendFailureKind failure) => failure != CaptureBackendFailureKind.Cancelled;
}
