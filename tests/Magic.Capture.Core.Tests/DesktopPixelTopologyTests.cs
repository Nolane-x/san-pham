using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class DesktopPixelTopologyTests
{
    [Fact]
    public void NegativeCoordinateMonitor_RoundTripsLocalAndDesktopPixels()
    {
        var monitor = new DesktopPixelMonitor("LEFT", new PixelRect(-1920, 0, 1920, 1080), 120, 120, false);
        var topology = new DesktopPixelTopology(new PixelRect(-1920, 0, 3840, 1080), [monitor]);
        var local = new PixelRect(100, 50, 800, 600);

        var desktop = topology.ToDesktopBounds(monitor, local);
        var roundTrip = topology.ToLocalBounds(monitor, desktop);

        Assert.Equal(new PixelRect(-1820, 50, 800, 600), desktop);
        Assert.Equal(local, roundTrip);
    }

    [Fact]
    public void PortraitMonitor_IsValidAndClipsInPhysicalPixels()
    {
        var monitor = new DesktopPixelMonitor("PORTRAIT", new PixelRect(1920, -300, 1080, 1920), 144, 144, false);
        var topology = new DesktopPixelTopology(new PixelRect(0, -300, 3000, 1920), [monitor]);

        var clipped = topology.ClipToDesktop(new PixelRect(2900, 1500, 500, 500));

        Assert.Equal(new PixelRect(2900, 1500, 100, 120), clipped);
        Assert.Equal(1.5, monitor.ScaleX, 6);
        Assert.Equal(1.5, monitor.ScaleY, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Monitor_RejectsInvalidDpi(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DesktopPixelMonitor("BAD", new PixelRect(0, 0, 100, 100), dpi, 96, true));
    }
}
