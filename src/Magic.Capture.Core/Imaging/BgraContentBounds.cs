using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Imaging;

public static class BgraContentBounds
{
    public static PixelRect Find(ReadOnlySpan<byte> bgra, int width, int height, int tolerance = 12, CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("BGRA buffer dimensions do not match the supplied image size.");
        tolerance = Math.Clamp(tolerance, 0, 64);
        var corners = new[] { 0, (width - 1) * 4, ((height - 1) * width) * 4, ((height * width) - 1) * 4 };
        var bb = Median(new byte[] { bgra[corners[0]], bgra[corners[1]], bgra[corners[2]], bgra[corners[3]] });
        var bg = Median(new byte[] { bgra[corners[0] + 1], bgra[corners[1] + 1], bgra[corners[2] + 1], bgra[corners[3] + 1] });
        var br = Median(new byte[] { bgra[corners[0] + 2], bgra[corners[1] + 2], bgra[corners[2] + 2], bgra[corners[3] + 2] });
        var minX = width; var minY = height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            if ((y & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (bgra[i + 3] <= 8) continue;
                if (Math.Abs(bgra[i] - bb) <= tolerance && Math.Abs(bgra[i + 1] - bg) <= tolerance && Math.Abs(bgra[i + 2] - br) <= tolerance) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX || maxY < minY ? new PixelRect(0, 0, width, height) : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static byte Median(byte[] values)
    {
        Array.Sort(values);
        return (byte)((values[1] + values[2]) / 2);
    }
}
