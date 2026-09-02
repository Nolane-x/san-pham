using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal static class BitmapContentBounds
{
    public static PixelRect Find(Bitmap bitmap, int tolerance = 12, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        tolerance = Math.Clamp(tolerance, 0, 64);
        var colors = new[] { bitmap.GetPixel(0, 0), bitmap.GetPixel(bitmap.Width - 1, 0), bitmap.GetPixel(0, bitmap.Height - 1), bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1) };
        byte Median(Func<Color, byte> selector)
        {
            var values = colors.Select(selector).OrderBy(v => v).ToArray();
            return (byte)((values[1] + values[2]) / 2);
        }
        var bb = Median(c => c.B); var bg = Median(c => c.G); var br = Median(c => c.R);
        var minX = bitmap.Width; var minY = bitmap.Height; var maxX = -1; var maxY = -1;
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var row = new byte[checked(bitmap.Width * 4)];
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if ((y & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                Marshal.Copy(IntPtr.Add(data.Scan0, BitmapStridePolicy.RowOffset(y, data.Stride)), row, 0, row.Length);
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var i = x * 4;
                    if (row[i + 3] <= 8) continue;
                    if (Math.Abs(row[i] - bb) <= tolerance && Math.Abs(row[i + 1] - bg) <= tolerance && Math.Abs(row[i + 2] - br) <= tolerance) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return maxX < minX || maxY < minY ? new PixelRect(0, 0, bitmap.Width, bitmap.Height) : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
