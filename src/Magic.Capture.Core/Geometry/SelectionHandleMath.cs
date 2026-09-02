namespace Magic.Capture.Core.Geometry;

public enum SelectionResizeHandle
{
    NorthWest,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West
}

public static class SelectionHandleMath
{
    public static PixelRect Resize(PixelRect current, SelectionResizeHandle handle, PixelPoint cursor, PixelRect limit)
    {
        if (current.IsEmpty || limit.IsEmpty) return PixelRect.Empty;
        var x = Math.Clamp(cursor.X, limit.X, limit.Right);
        var y = Math.Clamp(cursor.Y, limit.Y, limit.Bottom);

        var left = current.X;
        var right = current.Right;
        var top = current.Y;
        var bottom = current.Bottom;

        if (handle is SelectionResizeHandle.NorthWest or SelectionResizeHandle.West or SelectionResizeHandle.SouthWest)
        {
            left = Math.Min(x, current.Right);
            right = Math.Max(x, current.Right);
        }
        else if (handle is SelectionResizeHandle.NorthEast or SelectionResizeHandle.East or SelectionResizeHandle.SouthEast)
        {
            left = Math.Min(current.X, x);
            right = Math.Max(current.X, x);
        }

        if (handle is SelectionResizeHandle.NorthWest or SelectionResizeHandle.North or SelectionResizeHandle.NorthEast)
        {
            top = Math.Min(y, current.Bottom);
            bottom = Math.Max(y, current.Bottom);
        }
        else if (handle is SelectionResizeHandle.SouthWest or SelectionResizeHandle.South or SelectionResizeHandle.SouthEast)
        {
            top = Math.Min(current.Y, y);
            bottom = Math.Max(current.Y, y);
        }

        if (right == left) right = Math.Min(limit.Right, left + 1);
        if (bottom == top) bottom = Math.Min(limit.Bottom, top + 1);
        if (right == left && left > limit.X) left--;
        if (bottom == top && top > limit.Y) top--;

        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)).Intersect(limit);
    }

    public static PixelPoint OppositeCorner(PixelRect current, SelectionResizeHandle handle) => handle switch
    {
        SelectionResizeHandle.NorthWest => new PixelPoint(current.Right, current.Bottom),
        SelectionResizeHandle.NorthEast => new PixelPoint(current.X, current.Bottom),
        SelectionResizeHandle.SouthEast => new PixelPoint(current.X, current.Y),
        SelectionResizeHandle.SouthWest => new PixelPoint(current.Right, current.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "Only corner handles have a single opposite corner.")
    };

    public static bool IsCorner(SelectionResizeHandle handle) => handle is
        SelectionResizeHandle.NorthWest or SelectionResizeHandle.NorthEast or SelectionResizeHandle.SouthEast or SelectionResizeHandle.SouthWest;
}
