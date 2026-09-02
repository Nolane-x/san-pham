namespace Magic.Capture.Core.Imaging;

public sealed record PixelStatisticsResult(
    int Width,
    int Height,
    long PixelCount,
    double MeanRed,
    double MeanGreen,
    double MeanBlue,
    double MeanAlpha,
    double OpaquePixelPercent,
    byte MinimumRed,
    byte MinimumGreen,
    byte MinimumBlue,
    byte MaximumRed,
    byte MaximumGreen,
    byte MaximumBlue);

public static class PixelStatistics
{
    public static PixelStatisticsResult ComputeBgra(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var count = checked((long)width * height);
        var required = checked((int)(count * 4));
        if (pixels.Length != required) throw new ArgumentException("BGRA buffer length does not match the dimensions.", nameof(pixels));

        ulong red = 0, green = 0, blue = 0, alpha = 0;
        long opaque = 0;
        byte minR = 255, minG = 255, minB = 255;
        byte maxR = 0, maxG = 0, maxB = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];
            blue += b; green += g; red += r; alpha += a;
            if (a == 255) opaque++;
            minR = Math.Min(minR, r); minG = Math.Min(minG, g); minB = Math.Min(minB, b);
            maxR = Math.Max(maxR, r); maxG = Math.Max(maxG, g); maxB = Math.Max(maxB, b);
        }

        return new PixelStatisticsResult(
            width, height, count,
            red / (double)count, green / (double)count, blue / (double)count, alpha / (double)count,
            opaque * 100d / count,
            minR, minG, minB, maxR, maxG, maxB);
    }
}
