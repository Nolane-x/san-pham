using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Utilities;

public enum ImageCombineMode { Horizontal, Vertical, Grid }

public static class ImageCombineLayout
{
    public static IReadOnlyList<PixelRect> Create(IReadOnlyList<(int Width, int Height)> sizes, ImageCombineMode mode, int spacing = 0, int gridColumns = 2)
    {
        if (sizes.Count == 0) return [];
        spacing = Math.Max(0, spacing);
        gridColumns = Math.Max(1, gridColumns);
        var result = new List<PixelRect>(sizes.Count);

        switch (mode)
        {
            case ImageCombineMode.Horizontal:
            {
                var x = 0;
                foreach (var (width, height) in sizes)
                {
                    var w = Math.Max(1, width);
                    var h = Math.Max(1, height);
                    result.Add(new PixelRect(x, 0, w, h));
                    x += w + spacing;
                }
                break;
            }
            case ImageCombineMode.Vertical:
            {
                var y = 0;
                foreach (var (width, height) in sizes)
                {
                    var w = Math.Max(1, width);
                    var h = Math.Max(1, height);
                    result.Add(new PixelRect(0, y, w, h));
                    y += h + spacing;
                }
                break;
            }
            default:
            {
                var cellWidth = sizes.Max(s => Math.Max(1, s.Width));
                var cellHeight = sizes.Max(s => Math.Max(1, s.Height));
                for (var i = 0; i < sizes.Count; i++)
                {
                    var row = i / gridColumns;
                    var col = i % gridColumns;
                    var (width, height) = sizes[i];
                    result.Add(new PixelRect(col * (cellWidth + spacing), row * (cellHeight + spacing), Math.Max(1, width), Math.Max(1, height)));
                }
                break;
            }
        }

        return result;
    }
}
