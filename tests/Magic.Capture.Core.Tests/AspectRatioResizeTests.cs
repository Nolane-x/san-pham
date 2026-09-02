using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class AspectRatioResizeTests
{
    [Fact]
    public void Right_edge_keeps_left_and_vertical_center_while_preserving_ratio()
    {
        var proposed = new PixelRect(100, 100, 800, 500);

        var result = AspectRatioResize.Constrain(proposed, ResizeEdge.Right, 16d / 9d);

        Assert.Equal(100, result.X);
        Assert.Equal(proposed.Center.Y, result.Center.Y);
        Assert.Equal(800, result.Width);
        Assert.Equal(450, result.Height);
    }

    [Fact]
    public void Bottom_edge_keeps_top_and_horizontal_center_while_preserving_ratio()
    {
        var proposed = new PixelRect(100, 100, 700, 360);

        var result = AspectRatioResize.Constrain(proposed, ResizeEdge.Bottom, 16d / 9d);

        Assert.Equal(100, result.Y);
        Assert.Equal(proposed.Center.X, result.Center.X);
        Assert.Equal(640, result.Width);
        Assert.Equal(360, result.Height);
    }

    [Fact]
    public void Top_left_corner_keeps_opposite_corner_fixed()
    {
        var proposed = new PixelRect(250, 200, 640, 420);
        var fixedRight = proposed.Right;
        var fixedBottom = proposed.Bottom;

        var result = AspectRatioResize.Constrain(proposed, ResizeEdge.TopLeft, 4d / 3d);

        Assert.Equal(fixedRight, result.Right);
        Assert.Equal(fixedBottom, result.Bottom);
        Assert.InRange(Math.Abs(result.Width / (double)result.Height - 4d / 3d), 0, 0.01);
    }

    [Fact]
    public void Resize_never_drops_below_minimums()
    {
        var proposed = new PixelRect(10, 20, 20, 20);

        var result = AspectRatioResize.Constrain(proposed, ResizeEdge.BottomRight, 16d / 9d, 240, 140);

        Assert.True(result.Width >= 240);
        Assert.True(result.Height >= 140);
        Assert.InRange(Math.Abs(result.Width / (double)result.Height - 16d / 9d), 0, 0.01);
    }
}
