namespace Magic.Capture.Core.Geometry;

public enum ResizeEdge : uint
{
    Left = 1,
    Right = 2,
    Top = 3,
    TopLeft = 4,
    TopRight = 5,
    Bottom = 6,
    BottomLeft = 7,
    BottomRight = 8,
}

public static class AspectRatioResize
{
    public static PixelRect Constrain(
        PixelRect proposed,
        ResizeEdge edge,
        double aspectRatio,
        int minimumWidth = 160,
        int minimumHeight = 100)
    {
        if (aspectRatio <= 0 || double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));

        minimumWidth = Math.Max(1, minimumWidth);
        minimumHeight = Math.Max(1, minimumHeight);

        var minimumWidthForRatio = Math.Max(minimumWidth, (int)Math.Ceiling(minimumHeight * aspectRatio));
        var minimumHeightForRatio = Math.Max(minimumHeight, (int)Math.Ceiling(minimumWidth / aspectRatio));

        return edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => FromWidth(proposed, edge, aspectRatio, minimumWidthForRatio),
            ResizeEdge.Top or ResizeEdge.Bottom => FromHeight(proposed, edge, aspectRatio, minimumHeightForRatio),
            ResizeEdge.TopLeft or ResizeEdge.TopRight or ResizeEdge.BottomLeft or ResizeEdge.BottomRight =>
                FromCorner(proposed, edge, aspectRatio, minimumWidthForRatio, minimumHeightForRatio),
            _ => proposed,
        };
    }

    private static PixelRect FromWidth(PixelRect proposed, ResizeEdge edge, double ratio, int minimumWidth)
    {
        var width = Math.Max(minimumWidth, proposed.Width);
        var height = Math.Max(1, (int)Math.Round(width / ratio));
        var y = proposed.Center.Y - height / 2;
        var x = edge == ResizeEdge.Left ? proposed.Right - width : proposed.X;
        return new PixelRect(x, y, width, height);
    }

    private static PixelRect FromHeight(PixelRect proposed, ResizeEdge edge, double ratio, int minimumHeight)
    {
        var height = Math.Max(minimumHeight, proposed.Height);
        var width = Math.Max(1, (int)Math.Round(height * ratio));
        var x = proposed.Center.X - width / 2;
        var y = edge == ResizeEdge.Top ? proposed.Bottom - height : proposed.Y;
        return new PixelRect(x, y, width, height);
    }

    private static PixelRect FromCorner(
        PixelRect proposed,
        ResizeEdge edge,
        double ratio,
        int minimumWidth,
        int minimumHeight)
    {
        var widthCandidate = Math.Max(minimumWidth, proposed.Width);
        var heightFromWidth = Math.Max(minimumHeight, (int)Math.Round(widthCandidate / ratio));
        widthCandidate = Math.Max(minimumWidth, (int)Math.Round(heightFromWidth * ratio));

        var heightCandidate = Math.Max(minimumHeight, proposed.Height);
        var widthFromHeight = Math.Max(minimumWidth, (int)Math.Round(heightCandidate * ratio));
        heightCandidate = Math.Max(minimumHeight, (int)Math.Round(widthFromHeight / ratio));

        var widthCorrection = Math.Abs(heightFromWidth - proposed.Height);
        var heightCorrection = Math.Abs(widthFromHeight - proposed.Width);
        var useWidth = widthCorrection <= heightCorrection;

        var width = useWidth ? widthCandidate : widthFromHeight;
        var height = useWidth ? heightFromWidth : heightCandidate;

        var x = edge is ResizeEdge.TopLeft or ResizeEdge.BottomLeft
            ? proposed.Right - width
            : proposed.X;
        var y = edge is ResizeEdge.TopLeft or ResizeEdge.TopRight
            ? proposed.Bottom - height
            : proposed.Y;

        return new PixelRect(x, y, width, height);
    }
}
