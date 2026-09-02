using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Capture;

internal sealed record WindowCaptureTarget(
    IntPtr Handle,
    PixelRect Bounds,
    string Title,
    string? ProcessName,
    string? ExecutablePath = null,
    string? ClassName = null,
    uint ProcessId = 0,
    int ZOrder = 0)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName) ? Title : $"{Title}  ·  {ProcessName}";
    public override string ToString() => DisplayName;
}
