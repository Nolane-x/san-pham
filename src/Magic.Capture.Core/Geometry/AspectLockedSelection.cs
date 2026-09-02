namespace Magic.Capture.Core.Geometry;

public static class AspectLockedSelection
{
    public static PixelRect FromDrag(PixelPoint start, PixelPoint current, double aspectRatio, PixelRect bounds)
    {
        if (aspectRatio <= 0 || bounds.IsEmpty) return PixelRect.Empty;

        var signX = current.X >= start.X ? 1 : -1;
        var signY = current.Y >= start.Y ? 1 : -1;
        var rawWidth = Math.Abs(current.X - start.X);
        var rawHeight = Math.Abs(current.Y - start.Y);
        if (rawWidth == 0 || rawHeight == 0) return PixelRect.Empty;

        var width = rawWidth;
        var height = (int)Math.Round(width / aspectRatio);
        if (height > rawHeight)
        {
            height = rawHeight;
            width = (int)Math.Round(height * aspectRatio);
        }

        var maxWidth = signX > 0 ? bounds.Right - start.X : start.X - bounds.X;
        var maxHeight = signY > 0 ? bounds.Bottom - start.Y : start.Y - bounds.Y;
        width = Math.Min(width, Math.Max(0, maxWidth));
        height = Math.Min(height, Math.Max(0, maxHeight));

        if (width <= 0 || height <= 0) return PixelRect.Empty;

        var constrainedHeight = (int)Math.Round(width / aspectRatio);
        if (constrainedHeight <= maxHeight)
        {
            height = constrainedHeight;
        }
        else
        {
            height = maxHeight;
            width = (int)Math.Round(height * aspectRatio);
        }

        var x = signX > 0 ? start.X : start.X - width;
        var y = signY > 0 ? start.Y : start.Y - height;
        return new PixelRect(x, y, width, height).Intersect(bounds);
    }
}
