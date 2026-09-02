using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureSelectionGeometryTests
{
    private static readonly PixelRect Source = new(0, 0, 100, 80);

    [Fact]
    public void TryCreateBox_ClampsToSourceAndPreservesKind()
    {
        Assert.True(CaptureSelectionGeometryRules.TryCreateBox(
            CaptureSelectionKind.Ellipse, new PixelRect(-10, 10, 40, 30), Source,
            out var geometry, out _));
        Assert.NotNull(geometry);
        Assert.Equal(CaptureSelectionKind.Ellipse, geometry!.Kind);
        Assert.Equal(new PixelRect(0, 10, 30, 30), geometry.Bounds);
    }

    [Fact]
    public void TryCreatePath_RequiresThreeUsableVerticesAndBoundsPoints()
    {
        Assert.False(CaptureSelectionGeometryRules.TryCreatePath(
            CaptureSelectionKind.Polygon, [new(1, 1), new(2, 2)], Source, out _, out _));

        Assert.True(CaptureSelectionGeometryRules.TryCreatePath(
            CaptureSelectionKind.Polygon, [new(-10, 1), new(50, 2), new(120, 79)], Source,
            out var geometry, out _));
        Assert.All(geometry!.Points, point => Assert.True(Source.Contains(point)));
        Assert.Equal(3, geometry.Points.Count);
    }

    [Fact]
    public void TryCreatePath_BoundsAndDeduplicatesFreehandSamples()
    {
        var raw = Enumerable.Range(0, 5_000).Select(i => new PixelPoint(i % 100, i % 80));
        Assert.True(CaptureSelectionGeometryRules.TryCreatePath(
            CaptureSelectionKind.Freehand, raw, Source, out var geometry, out _));
        Assert.InRange(geometry!.Points.Count, 3, CaptureSelectionGeometryRules.MaximumPathPoints);
        Assert.True(geometry.Bounds.Width >= 2);
        Assert.True(geometry.Bounds.Height >= 2);
    }

    [Fact]
    public void TryCreatePath_RejectsZeroAreaPath()
    {
        var collinear = new[] { new PixelPoint(10, 10), new PixelPoint(20, 20), new PixelPoint(30, 30) };

        Assert.False(CaptureSelectionGeometryRules.TryCreatePath(
            CaptureSelectionKind.Polygon, collinear, Source, out _, out var polygonError));
        Assert.Contains("area", polygonError, StringComparison.OrdinalIgnoreCase);

        Assert.False(CaptureSelectionGeometryRules.TryCreatePath(
            CaptureSelectionKind.Freehand, collinear, Source, out _, out var freehandError));
        Assert.Contains("area", freehandError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateMultiRegion_DeduplicatesClampsAndCapsRegions()
    {
        var regions = Enumerable.Range(0, 40)
            .Select(i => new PixelRect(i, i % 40, 10, 10))
            .Prepend(new PixelRect(-5, -5, 10, 10));
        Assert.True(CaptureSelectionGeometryRules.TryCreateMultiRegion(regions, Source, out var geometry, out _));
        Assert.InRange(geometry!.Regions.Count, 1, CaptureSelectionGeometryRules.MaximumRegions);
        Assert.All(geometry.Regions, region => Assert.Equal(region, region.Intersect(Source)));
    }
}
