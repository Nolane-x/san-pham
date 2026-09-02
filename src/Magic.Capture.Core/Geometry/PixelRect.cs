namespace Magic.Capture.Core.Geometry;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Empty => new(0, 0, 0, 0);

    public int Right => X + Math.Max(0, Width);
    public int Bottom => Y + Math.Max(0, Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public PixelPoint Center => new(X + Width / 2, Y + Height / 2);

    public bool Contains(PixelPoint point) =>
        !IsEmpty && point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top
            ? Empty
            : new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}
