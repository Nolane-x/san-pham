using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed class ScreenCaptureService
{
    private readonly CaptureBackendRouter _router;

    public ScreenCaptureService(CaptureBackendRouter router) => _router = router;

    public CaptureAsset Capture(
        PixelRect bounds,
        CaptureSourceKind kind,
        string? sourceName = null,
        bool includeCursor = false,
        string? windowTitle = null,
        string? processName = null,
        string? monitorName = null,
        string? executablePath = null,
        IntPtr windowHandle = default,
        IntPtr monitorHandle = default,
        PixelRect? sourceBounds = null,
        CaptureTargetKind? targetKind = null,
        CaptureBackendPreference backendPreference = CaptureBackendPreference.Auto) =>
        CaptureWithDiagnostics(bounds, kind, sourceName, includeCursor, windowTitle, processName, monitorName, executablePath,
            windowHandle, monitorHandle, sourceBounds, targetKind, backendPreference).Asset;

    public CaptureWithDiagnosticsResult CaptureWithDiagnostics(
        PixelRect bounds,
        CaptureSourceKind kind,
        string? sourceName = null,
        bool includeCursor = false,
        string? windowTitle = null,
        string? processName = null,
        string? monitorName = null,
        string? executablePath = null,
        IntPtr windowHandle = default,
        IntPtr monitorHandle = default,
        PixelRect? sourceBounds = null,
        CaptureTargetKind? targetKind = null,
        CaptureBackendPreference backendPreference = CaptureBackendPreference.Auto)
    {
        if (bounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(bounds));
        var request = new CaptureBackendRequest(
            bounds,
            targetKind ?? ToTargetKind(kind),
            includeCursor,
            windowHandle,
            monitorHandle,
            sourceBounds);
        var routed = _router.CaptureAsync(request, backendPreference, CancellationToken.None).GetAwaiter().GetResult();
        if (!PngDimensions.TryRead(routed.Frame.PngBytes, out var width, out var height) || width != bounds.Width || height != bounds.Height)
            throw new InvalidDataException("Capture router returned unexpected output dimensions.");

        var asset = CaptureAsset.Create(bounds, routed.Frame.PngBytes, kind, sourceName, windowTitle, processName, monitorName, executablePath: executablePath);
        var failures = routed.Attempts
            .Where(item => !item.Succeeded && !string.IsNullOrWhiteSpace(item.Message))
            .Select(item => $"{item.Backend}: {item.Message}")
            .ToArray();
        return new CaptureWithDiagnosticsResult(
            asset,
            new CaptureAttemptDiagnostics(
                routed.Backend.ToString(),
                routed.Attempts.Count,
                bounds,
                failures,
                routed.Attempts));
    }

    internal static CaptureTargetKind ToTargetKind(CaptureSourceKind kind) => kind switch
    {
        CaptureSourceKind.Window => CaptureTargetKind.Window,
        CaptureSourceKind.Monitor => CaptureTargetKind.Monitor,
        CaptureSourceKind.VirtualDesktop => CaptureTargetKind.VirtualDesktop,
        _ => CaptureTargetKind.RegionSingleMonitor
    };
}
