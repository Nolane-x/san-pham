using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingTarget
{
    public RecordingTarget(
        RecordingTargetKind kind,
        PixelRect bounds,
        string displayName,
        IntPtr windowHandle = default,
        IntPtr monitorHandle = default,
        string? monitorName = null)
    {
        if (bounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Recording target display name is required.", nameof(displayName));
        if (kind == RecordingTargetKind.Window && windowHandle == IntPtr.Zero)
            throw new ArgumentException("Window recording requires an HWND.", nameof(windowHandle));
        if (kind == RecordingTargetKind.Monitor && monitorHandle == IntPtr.Zero)
            throw new ArgumentException("Monitor recording requires an HMONITOR.", nameof(monitorHandle));
        Kind = kind;
        Bounds = bounds;
        DisplayName = displayName.Trim();
        WindowHandle = windowHandle;
        MonitorHandle = monitorHandle;
        MonitorName = monitorName;
    }

    public RecordingTargetKind Kind { get; }
    public PixelRect Bounds { get; }
    public string DisplayName { get; }
    public IntPtr WindowHandle { get; }
    public IntPtr MonitorHandle { get; }
    public string? MonitorName { get; }
}
