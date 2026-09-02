using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Utilities;

public static class ImageSplitPlan
{
    public static IReadOnlyList<PixelRect> Create(int width, int height, int rows, int columns)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (rows <= 0 || columns <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (rows > height || columns > width) throw new ArgumentException("Rows/columns cannot create empty cells.");

        var result = new List<PixelRect>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            var y0 = row * height / rows;
            var y1 = (row + 1) * height / rows;
            for (var col = 0; col < columns; col++)
            {
                var x0 = col * width / columns;
                var x1 = (col + 1) * width / columns;
                result.Add(new PixelRect(x0, y0, x1 - x0, y1 - y0));
            }
        }
        return result;
    }
}
