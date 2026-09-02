using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Capture;

public static class CaptureSnapRules
{
    public static PixelRect SelectSmallestContaining(IEnumerable<PixelRect>? candidates, PixelPoint point)
    {
        var best = PixelRect.Empty;
        long bestArea = long.MaxValue;
        foreach (var candidate in candidates ?? [])
        {
            if (candidate.IsEmpty || point.X < candidate.X || point.Y < candidate.Y || point.X >= candidate.Right || point.Y >= candidate.Bottom) continue;
            var area = (long)candidate.Width * candidate.Height;
            if (area >= bestArea) continue;
            best = candidate;
            bestArea = area;
        }
        return best;
    }

    public static PixelRect SnapEdges(
        PixelRect selection,
        IEnumerable<PixelRect>? candidates,
        PixelRect sourceBounds,
        int threshold = 8)
    {
        selection = selection.Intersect(sourceBounds);
        if (selection.IsEmpty || threshold <= 0) return selection;

        var edgesX = new List<int> { sourceBounds.X, sourceBounds.Right };
        var edgesY = new List<int> { sourceBounds.Y, sourceBounds.Bottom };
        var scanned = 0;
        foreach (var candidate in candidates ?? [])
        {
            if (scanned++ >= 512) break;
            var clipped = candidate.Intersect(sourceBounds);
            if (clipped.IsEmpty) continue;
            edgesX.Add(clipped.X);
            edgesX.Add(clipped.Right);
            edgesY.Add(clipped.Y);
            edgesY.Add(clipped.Bottom);
        }

        var left = NearestEdge(selection.X, edgesX, threshold);
        var right = NearestEdge(selection.Right, edgesX, threshold);
        var top = NearestEdge(selection.Y, edgesY, threshold);
        var bottom = NearestEdge(selection.Bottom, edgesY, threshold);
        if (right - left < 2) { left = selection.X; right = selection.Right; }
        if (bottom - top < 2) { top = selection.Y; bottom = selection.Bottom; }
        return new PixelRect(left, top, right - left, bottom - top).Intersect(sourceBounds);
    }

    private static int NearestEdge(int value, IReadOnlyList<int> edges, int threshold)
    {
        var best = value;
        var bestDistance = threshold + 1;
        foreach (var edge in edges)
        {
            var distance = Math.Abs((long)edge - value);
            if (distance > threshold || distance >= bestDistance) continue;
            best = edge;
            bestDistance = (int)distance;
        }
        return best;
    }

}
