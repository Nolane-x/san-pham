using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Capture;

public enum CaptureSelectionKind
{
    Rectangle,
    Ellipse,
    Polygon,
    Freehand,
    MultiRegion
}

public enum MultiRegionOutputMode
{
    Canvas,
    SeparateImages
}

public sealed record CaptureSelectionGeometry(
    CaptureSelectionKind Kind,
    PixelRect Bounds,
    IReadOnlyList<PixelPoint> Points,
    IReadOnlyList<PixelRect> Regions)
{
    public static CaptureSelectionGeometry Rectangle(PixelRect bounds) =>
        new(CaptureSelectionKind.Rectangle, bounds, [], []);
}

public static class CaptureSelectionGeometryRules
{
    public const int MaximumPathPoints = 2_048;
    public const int MaximumRegions = 16;
    public const int MinimumDimension = 2;
    private const int MinimumFreehandDistanceSquared = 4;

    public static bool TryCreateBox(
        CaptureSelectionKind kind,
        PixelRect requested,
        PixelRect sourceBounds,
        out CaptureSelectionGeometry? geometry,
        out string? error)
    {
        geometry = null;
        error = null;
        if (kind is not CaptureSelectionKind.Rectangle and not CaptureSelectionKind.Ellipse)
        {
            error = "Box geometry is only valid for rectangle or ellipse capture.";
            return false;
        }
        var bounds = requested.Intersect(sourceBounds);
        if (!IsUsableBounds(bounds))
        {
            error = "Selection is too small.";
            return false;
        }
        geometry = new CaptureSelectionGeometry(kind, bounds, [], []);
        return true;
    }

    public static bool TryCreatePath(
        CaptureSelectionKind kind,
        IEnumerable<PixelPoint>? rawPoints,
        PixelRect sourceBounds,
        out CaptureSelectionGeometry? geometry,
        out string? error)
    {
        geometry = null;
        error = null;
        if (kind is not CaptureSelectionKind.Polygon and not CaptureSelectionKind.Freehand)
        {
            error = "Path geometry is only valid for polygon or freehand capture.";
            return false;
        }
        if (sourceBounds.IsEmpty)
        {
            error = "Capture source bounds are empty.";
            return false;
        }

        var points = NormalizePoints(rawPoints, sourceBounds, kind == CaptureSelectionKind.Freehand);
        if (points.Count < 3)
        {
            error = kind == CaptureSelectionKind.Polygon
                ? "Polygon capture needs at least three vertices."
                : "Freehand capture needs a closed usable region.";
            return false;
        }
        if (!HasNonZeroArea(points))
        {
            error = "Selection path must enclose a non-zero area.";
            return false;
        }

        var bounds = BoundsOf(points).Intersect(sourceBounds);
        if (!IsUsableBounds(bounds))
        {
            error = "Selection is too small.";
            return false;
        }

        geometry = new CaptureSelectionGeometry(kind, bounds, points, []);
        return true;
    }

    public static bool TryCreateMultiRegion(
        IEnumerable<PixelRect>? rawRegions,
        PixelRect sourceBounds,
        out CaptureSelectionGeometry? geometry,
        out string? error)
    {
        geometry = null;
        error = null;
        if (sourceBounds.IsEmpty)
        {
            error = "Capture source bounds are empty.";
            return false;
        }

        var regions = new List<PixelRect>(MaximumRegions);
        foreach (var candidate in rawRegions ?? [])
        {
            var region = candidate.Intersect(sourceBounds);
            if (!IsUsableBounds(region) || regions.Contains(region)) continue;
            regions.Add(region);
            if (regions.Count == MaximumRegions) break;
        }
        if (regions.Count == 0)
        {
            error = "Multi-region capture needs at least one region.";
            return false;
        }

        var union = PixelRect.Empty;
        foreach (var region in regions) union = PixelRect.Union(union, region);
        geometry = new CaptureSelectionGeometry(CaptureSelectionKind.MultiRegion, union, [], regions);
        return true;
    }

    public static bool ContainsPoint(CaptureSelectionGeometry geometry, PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Bounds.IsEmpty || !geometry.Bounds.Contains(point)) return false;

        return geometry.Kind switch
        {
            CaptureSelectionKind.Rectangle => true,
            CaptureSelectionKind.Ellipse => ContainsEllipsePoint(geometry.Bounds, point),
            CaptureSelectionKind.Polygon or CaptureSelectionKind.Freehand => ContainsPolygonPoint(geometry.Points, point),
            CaptureSelectionKind.MultiRegion => geometry.Regions.Any(region => region.Contains(point)),
            _ => false
        };
    }

    private static bool ContainsEllipsePoint(PixelRect bounds, PixelPoint point)
    {
        var radiusX = bounds.Width / 2d;
        var radiusY = bounds.Height / 2d;
        if (radiusX <= 0 || radiusY <= 0) return false;
        var centerX = bounds.X + radiusX;
        var centerY = bounds.Y + radiusY;
        var dx = (point.X + .5d - centerX) / radiusX;
        var dy = (point.Y + .5d - centerY) / radiusY;
        return dx * dx + dy * dy <= 1d;
    }

    private static bool ContainsPolygonPoint(IReadOnlyList<PixelPoint> points, PixelPoint point)
    {
        if (points.Count < 3) return false;
        var inside = false;
        var px = point.X + .5d;
        var py = point.Y + .5d;
        for (var i = 0; i < points.Count; i++)
        {
            var j = i == 0 ? points.Count - 1 : i - 1;
            var xi = (double)points[i].X;
            var yi = (double)points[i].Y;
            var xj = (double)points[j].X;
            var yj = (double)points[j].Y;
            var crosses = (yi > py) != (yj > py) &&
                          px < (xj - xi) * (py - yi) / (yj - yi) + xi;
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<PixelPoint> NormalizePoints(IEnumerable<PixelPoint>? rawPoints, PixelRect sourceBounds, bool simplify)
    {
        var result = new List<PixelPoint>(Math.Min(MaximumPathPoints, 256));
        PixelPoint? previous = null;
        foreach (var candidate in rawPoints ?? [])
        {
            var point = new PixelPoint(
                Math.Clamp(candidate.X, sourceBounds.X, sourceBounds.Right - 1),
                Math.Clamp(candidate.Y, sourceBounds.Y, sourceBounds.Bottom - 1));
            if (previous is { } last)
            {
                if (last == point) continue;
                if (simplify)
                {
                    var dx = point.X - last.X;
                    var dy = point.Y - last.Y;
                    if ((long)dx * dx + (long)dy * dy < MinimumFreehandDistanceSquared) continue;
                }
            }
            result.Add(point);
            previous = point;
            if (result.Count == MaximumPathPoints) break;
        }

        // A repeated closing point is redundant; the renderer closes the path explicitly.
        if (result.Count > 1 && result[0] == result[^1]) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static bool HasNonZeroArea(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count < 3) return false;
        var origin = points[0];
        for (var i = 1; i < points.Count - 1; i++)
        {
            var ax = (Int128)points[i].X - origin.X;
            var ay = (Int128)points[i].Y - origin.Y;
            for (var j = i + 1; j < points.Count; j++)
            {
                var bx = (Int128)points[j].X - origin.X;
                var by = (Int128)points[j].Y - origin.Y;
                if (ax * by - ay * bx != 0) return true;
            }
        }
        return false;
    }

    private static PixelRect BoundsOf(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count == 0) return PixelRect.Empty;
        var minX = points[0].X;
        var minY = points[0].Y;
        var maxX = points[0].X;
        var maxY = points[0].Y;
        for (var i = 1; i < points.Count; i++)
        {
            minX = Math.Min(minX, points[i].X);
            minY = Math.Min(minY, points[i].Y);
            maxX = Math.Max(maxX, points[i].X);
            maxY = Math.Max(maxY, points[i].Y);
        }
        return new PixelRect(minX, minY, checked(maxX - minX + 1), checked(maxY - minY + 1));
    }

    private static bool IsUsableBounds(PixelRect bounds) =>
        !bounds.IsEmpty && bounds.Width >= MinimumDimension && bounds.Height >= MinimumDimension;
}
