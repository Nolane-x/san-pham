using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class AspectLockedSelectionTests
{
    [Theory]
    [InlineData(160, 100, 1.0, 100, 100)]
    [InlineData(160, 100, 16.0 / 9.0, 160, 90)]
    [InlineData(100, 160, 4.0 / 3.0, 100, 75)]
    public void Locks_drag_to_requested_ratio(double dx, double dy, double ratio, int expectedWidth, int expectedHeight)
    {
        var rect = AspectLockedSelection.FromDrag(new PixelPoint(10, 10), new PixelPoint(10 + (int)dx, 10 + (int)dy), ratio, new PixelRect(0, 0, 500, 500));
        Assert.Equal(expectedWidth, rect.Width);
        Assert.Equal(expectedHeight, rect.Height);
    }

    [Fact]
    public void Clamps_selection_to_bounds()
    {
        var rect = AspectLockedSelection.FromDrag(new PixelPoint(450, 450), new PixelPoint(600, 600), 1.0, new PixelRect(0, 0, 500, 500));
        Assert.Equal(new PixelRect(450, 450, 50, 50), rect);
    }
}
