using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class SelectionHandleMathTests
{
    [Fact]
    public void Resize_west_moves_only_left_edge()
    {
        var current = new PixelRect(100, 80, 200, 120);
        var limit = new PixelRect(0, 0, 800, 600);

        var result = SelectionHandleMath.Resize(current, SelectionResizeHandle.West, new PixelPoint(60, 500), limit);

        Assert.Equal(new PixelRect(60, 80, 240, 120), result);
    }

    [Fact]
    public void Resize_south_east_clamps_to_monitor_bounds()
    {
        var current = new PixelRect(100, 80, 200, 120);
        var limit = new PixelRect(0, 0, 320, 220);

        var result = SelectionHandleMath.Resize(current, SelectionResizeHandle.SouthEast, new PixelPoint(999, 999), limit);

        Assert.Equal(new PixelRect(100, 80, 220, 140), result);
    }

    [Fact]
    public void Resize_corner_can_cross_the_opposite_anchor()
    {
        var current = new PixelRect(100, 100, 100, 100);
        var limit = new PixelRect(0, 0, 500, 500);

        var result = SelectionHandleMath.Resize(current, SelectionResizeHandle.NorthWest, new PixelPoint(260, 250), limit);

        Assert.Equal(new PixelRect(200, 200, 60, 50), result);
    }

    [Theory]
    [InlineData(SelectionResizeHandle.NorthWest, 300, 200)]
    [InlineData(SelectionResizeHandle.NorthEast, 100, 200)]
    [InlineData(SelectionResizeHandle.SouthEast, 100, 80)]
    [InlineData(SelectionResizeHandle.SouthWest, 300, 80)]
    public void Opposite_corner_is_stable_for_corner_handles(SelectionResizeHandle handle, int expectedX, int expectedY)
    {
        var rect = new PixelRect(100, 80, 200, 120);

        var anchor = SelectionHandleMath.OppositeCorner(rect, handle);

        Assert.Equal(new PixelPoint(expectedX, expectedY), anchor);
    }
}
