using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class SelectionMathTests
{
    [Fact]
    public void FromPoints_NormalizesReverseDrag()
    {
        var rect = SelectionMath.FromPoints(new PixelPoint(300, 220), new PixelPoint(100, 80));
        Assert.Equal(new PixelRect(100, 80, 200, 140), rect);
    }

    [Fact]
    public void FromPoints_PreservesNegativeDesktopCoordinates()
    {
        var rect = SelectionMath.FromPoints(new PixelPoint(-500, 40), new PixelPoint(-100, 340));
        Assert.Equal(new PixelRect(-500, 40, 400, 300), rect);
    }

    [Fact]
    public void Clamp_IntersectsSelectionWithMonitorBounds()
    {
        var bounds = new PixelRect(-1920, 0, 1920, 1080);
        var rect = new PixelRect(-2000, -20, 300, 200);
        Assert.Equal(new PixelRect(-1920, 0, 220, 180), SelectionMath.Clamp(rect, bounds));
    }

    [Fact]
    public void Clamp_ReturnsEmptyWhenDisjoint()
    {
        var bounds = new PixelRect(0, 0, 100, 100);
        Assert.True(SelectionMath.Clamp(new PixelRect(150, 150, 20, 20), bounds).IsEmpty);
    }
}
