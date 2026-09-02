namespace Magic.Capture.Core.Geometry;

public static class SelectionMath
{
    public static PixelRect FromPoints(PixelPoint a, PixelPoint b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect Clamp(PixelRect rect, PixelRect bounds) => rect.Intersect(bounds);

    public static PixelRect Nudge(PixelRect rect, int deltaX, int deltaY, PixelRect bounds) =>
        Clamp(new PixelRect(rect.X + deltaX, rect.Y + deltaY, rect.Width, rect.Height), bounds);
}
