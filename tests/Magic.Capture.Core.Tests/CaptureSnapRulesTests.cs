using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureSnapRulesTests
{
    [Fact]
    public void SelectSmallestContaining_prefers_inner_window()
    {
        var outer = new PixelRect(0, 0, 1000, 800);
        var inner = new PixelRect(100, 100, 300, 200);
        var result = CaptureSnapRules.SelectSmallestContaining([outer, inner], new PixelPoint(150, 150));
        Assert.Equal(inner, result);
    }

    [Fact]
    public void SelectSmallestContaining_ignores_empty_and_outside_rectangles()
    {
        var result = CaptureSnapRules.SelectSmallestContaining([PixelRect.Empty, new PixelRect(50, 50, 10, 10)], new PixelPoint(0, 0));
        Assert.Equal(PixelRect.Empty, result);
    }
    [Fact]
    public void SnapEdges_snaps_each_near_edge_to_window_or_desktop_edges()
    {
        var result = CaptureSnapRules.SnapEdges(
            new PixelRect(4, 97, 301, 206),
            [new PixelRect(0, 100, 300, 200)],
            new PixelRect(0, 0, 1920, 1080),
            threshold: 6);

        Assert.Equal(new PixelRect(0, 100, 300, 200), result);
    }

    [Fact]
    public void SnapEdges_does_not_jump_when_edge_is_outside_threshold()
    {
        var original = new PixelRect(20, 20, 250, 150);
        var result = CaptureSnapRules.SnapEdges(
            original,
            [new PixelRect(100, 100, 300, 200)],
            new PixelRect(0, 0, 1920, 1080),
            threshold: 6);

        Assert.Equal(original, result);
    }

}
