using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Capture;

internal sealed record CaptureBackendRequest(
    PixelRect Bounds,
    CaptureTargetKind TargetKind,
    bool IncludeCursor,
    IntPtr WindowHandle = default,
    IntPtr MonitorHandle = default,
    PixelRect? SourceBounds = null)
{
    public PixelRect BackendBounds => SourceBounds ?? Bounds;
}

internal sealed record CaptureBackendFrame(
    byte[] PngBytes,
    PixelRect FrameBounds,
    int RecoveryCount = 0);

internal sealed record CaptureBackendProbe(
    CaptureBackendKind Backend,
    bool IsAvailable,
    string? Reason = null);

internal interface ICaptureBackend
{
    CaptureBackendKind Kind { get; }
    CaptureBackendProbe Probe();
    Task<CaptureBackendFrame> CaptureAsync(CaptureBackendRequest request, CancellationToken cancellationToken);
}
