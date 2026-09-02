using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Capture;

public sealed record DesktopPixelMonitor
{
    public DesktopPixelMonitor(string deviceName, PixelRect bounds, double dpiX, double dpiY, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("Monitor device name is required.", nameof(deviceName));
        if (bounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(bounds), "Monitor bounds must have positive dimensions.");
        if (!double.IsFinite(dpiX) || dpiX <= 0) throw new ArgumentOutOfRangeException(nameof(dpiX));
        if (!double.IsFinite(dpiY) || dpiY <= 0) throw new ArgumentOutOfRangeException(nameof(dpiY));
        DeviceName = deviceName.Trim();
        Bounds = bounds;
        DpiX = dpiX;
        DpiY = dpiY;
        IsPrimary = isPrimary;
    }

    public string DeviceName { get; }
    public PixelRect Bounds { get; }
    public double DpiX { get; }
    public double DpiY { get; }
    public bool IsPrimary { get; }
    public double ScaleX => DpiX / 96d;
    public double ScaleY => DpiY / 96d;
}

public sealed class DesktopPixelTopology
{
    public DesktopPixelTopology(PixelRect virtualBounds, IReadOnlyList<DesktopPixelMonitor> monitors)
    {
        if (virtualBounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(virtualBounds));
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        if (monitors.Any(monitor => monitor.Bounds.IsEmpty)) throw new ArgumentException("All monitors must have positive physical-pixel bounds.", nameof(monitors));
        if (monitors.Any(monitor => monitor.Bounds.Intersect(virtualBounds).IsEmpty))
            throw new ArgumentException("Every monitor must intersect the virtual desktop bounds.", nameof(monitors));

        VirtualBounds = virtualBounds;
        Monitors = monitors.ToArray();
    }

    public PixelRect VirtualBounds { get; }
    public IReadOnlyList<DesktopPixelMonitor> Monitors { get; }

    public PixelRect ClipToDesktop(PixelRect requested) => requested.Intersect(VirtualBounds);

    public PixelRect ToDesktopBounds(DesktopPixelMonitor monitor, PixelRect localBounds)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (localBounds.IsEmpty) return PixelRect.Empty;
        var localDesktop = new PixelRect(0, 0, monitor.Bounds.Width, monitor.Bounds.Height);
        var clipped = localBounds.Intersect(localDesktop);
        if (clipped.IsEmpty) return PixelRect.Empty;
        return new PixelRect(
            checked(monitor.Bounds.X + clipped.X),
            checked(monitor.Bounds.Y + clipped.Y),
            clipped.Width,
            clipped.Height);
    }

    public PixelRect ToLocalBounds(DesktopPixelMonitor monitor, PixelRect desktopBounds)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var clipped = desktopBounds.Intersect(monitor.Bounds);
        if (clipped.IsEmpty) return PixelRect.Empty;
        return new PixelRect(
            checked(clipped.X - monitor.Bounds.X),
            checked(clipped.Y - monitor.Bounds.Y),
            clipped.Width,
            clipped.Height);
    }
}
