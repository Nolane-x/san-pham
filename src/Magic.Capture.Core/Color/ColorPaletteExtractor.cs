namespace Magic.Capture.Core.Color;

public sealed record ColorPaletteResult(ColorValue Average, ColorValue Dominant, IReadOnlyList<ColorValue> Colors, long SampledPixels);

public static class ColorPaletteExtractor
{
    public const int MaximumSampledPixels = 250_000;
    public const int MaximumPaletteColors = 16;

    public static ColorPaletteResult ExtractBgra(ReadOnlySpan<byte> bgra, int width, int height, int paletteSize = 8)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("BGRA buffer dimensions do not match the supplied image size.");
        paletteSize = Math.Clamp(paletteSize, 1, MaximumPaletteColors);
        var totalPixels = checked((long)width * height);
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(totalPixels / (double)MaximumSampledPixels)));
        var counts = new int[32 * 32 * 32];
        var sumR = new long[counts.Length]; var sumG = new long[counts.Length]; var sumB = new long[counts.Length];
        long allR = 0, allG = 0, allB = 0, sampled = 0;
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var i = (y * width + x) * 4;
                if (bgra[i + 3] < 16) continue;
                var b = bgra[i]; var g = bgra[i + 1]; var r = bgra[i + 2];
                var bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                counts[bin]++; sumR[bin] += r; sumG[bin] += g; sumB[bin] += b;
                allR += r; allG += g; allB += b; sampled++;
            }
        }
        if (sampled == 0) return new(ColorValue.FromRgb(0, 0, 0, 0), ColorValue.FromRgb(0, 0, 0, 0), [], 0);
        var bins = Enumerable.Range(0, counts.Length).Where(i => counts[i] > 0).OrderByDescending(i => counts[i]).ThenBy(i => i).Take(paletteSize).ToArray();
        var colors = bins.Select(i => ColorValue.FromRgb((byte)(sumR[i] / counts[i]), (byte)(sumG[i] / counts[i]), (byte)(sumB[i] / counts[i]))).ToArray();
        var average = ColorValue.FromRgb((byte)(allR / sampled), (byte)(allG / sampled), (byte)(allB / sampled));
        return new(average, colors[0], colors, sampled);
    }
}
