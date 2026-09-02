using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Capture;

public sealed record LastRegionState(PixelRect GlobalBounds, string? MonitorDeviceName, DateTimeOffset CapturedUtc)
{
    public bool IsUsableWithin(PixelRect virtualDesktopBounds) =>
        !GlobalBounds.IsEmpty && !GlobalBounds.Intersect(virtualDesktopBounds).IsEmpty;
}
