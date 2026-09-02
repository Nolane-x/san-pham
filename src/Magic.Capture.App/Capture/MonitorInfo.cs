using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Capture;

public sealed record MonitorInfo(IntPtr Handle, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, string DeviceName, double DpiX = 96, double DpiY = 96)
{
    public double ScaleX => DpiX / 96d;
    public double ScaleY => DpiY / 96d;
    public string DisplayName => $"{(IsPrimary ? "Primary · " : string.Empty)}{DeviceName} · {Bounds.Width}×{Bounds.Height} @ {Bounds.X},{Bounds.Y} · {Math.Round(ScaleX * 100):0}%";
    public override string ToString() => DisplayName;
}
