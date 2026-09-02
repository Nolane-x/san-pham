using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal static class PixelBufferEffects
{
    public static void Pixelate(Bitmap bitmap, PixelRect requested, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        blockSize = Math.Clamp(blockSize, 2, 256);
        var bounds = requested.Intersect(new PixelRect(0, 0, bitmap.Width, bitmap.Height));
        if (bounds.IsEmpty) return;
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(bounds.Width, bounds.Height);
        TransformRegion(bitmap, bounds, pixels =>
        {
            var width = bounds.Width;
            var height = bounds.Height;
            for (var y = 0; y < height; y += blockSize)
            {
                var blockHeight = Math.Min(blockSize, height - y);
                for (var x = 0; x < width; x += blockSize)
                {
                    var blockWidth = Math.Min(blockSize, width - x);
                    var sampleX = x + Math.Min(blockWidth - 1, blockWidth / 2);
                    var sampleY = y + Math.Min(blockHeight - 1, blockHeight / 2);
                    var sample = (sampleY * width + sampleX) * 4;
                    var b = pixels[sample];
                    var g = pixels[sample + 1];
                    var r = pixels[sample + 2];
                    var a = pixels[sample + 3];
                    for (var py = y; py < y + blockHeight; py++)
                    {
                        var offset = (py * width + x) * 4;
                        for (var px = 0; px < blockWidth; px++, offset += 4)
                        {
                            pixels[offset] = b;
                            pixels[offset + 1] = g;
                            pixels[offset + 2] = r;
                            pixels[offset + 3] = a;
                        }
                    }
                }
            }
            return pixels;
        });
    }

    public static void BoxBlur(Bitmap bitmap, PixelRect requested, int radius)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        radius = Math.Clamp(radius, 1, 128);
        var bounds = requested.Intersect(new PixelRect(0, 0, bitmap.Width, bitmap.Height));
        if (bounds.IsEmpty) return;
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(bounds.Width, bounds.Height);
        TransformRegion(bitmap, bounds, source =>
        {
            var width = bounds.Width;
            var height = bounds.Height;
            var horizontal = new byte[source.Length];

            for (var y = 0; y < height; y++)
            {
                var row = y * width * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    long sum = 0;
                    var left = 0;
                    var right = -1;
                    for (var x = 0; x < width; x++)
                    {
                        var desiredLeft = Math.Max(0, x - radius);
                        var desiredRight = Math.Min(width - 1, x + radius);
                        while (right < desiredRight)
                        {
                            right++;
                            sum += source[row + right * 4 + channel];
                        }
                        while (left < desiredLeft)
                        {
                            sum -= source[row + left * 4 + channel];
                            left++;
                        }
                        horizontal[row + x * 4 + channel] = (byte)(sum / (right - left + 1));
                    }
                }
            }

            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    long sum = 0;
                    var top = 0;
                    var bottom = -1;
                    for (var y = 0; y < height; y++)
                    {
                        var desiredTop = Math.Max(0, y - radius);
                        var desiredBottom = Math.Min(height - 1, y + radius);
                        while (bottom < desiredBottom)
                        {
                            bottom++;
                            sum += horizontal[(bottom * width + x) * 4 + channel];
                        }
                        while (top < desiredTop)
                        {
                            sum -= horizontal[(top * width + x) * 4 + channel];
                            top++;
                        }
                        source[(y * width + x) * 4 + channel] = (byte)(sum / (bottom - top + 1));
                    }
                }
            }
            return source;
        });
    }

    private static void TransformRegion(Bitmap bitmap, PixelRect region, Func<byte[], byte[]> transform)
    {
        var rect = new Rectangle(region.X, region.Y, region.Width, region.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = region.Width * 4;
            var pixels = new byte[checked(rowBytes * region.Height)];
            for (var y = 0; y < region.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, BitmapStridePolicy.RowOffset(y, data.Stride)), pixels, y * rowBytes, rowBytes);

            var output = transform(pixels);
            if (output.Length != pixels.Length) throw new InvalidOperationException("Pixel transform changed the buffer length.");
            for (var y = 0; y < region.Height; y++)
                Marshal.Copy(output, y * rowBytes, IntPtr.Add(data.Scan0, BitmapStridePolicy.RowOffset(y, data.Stride)), rowBytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
