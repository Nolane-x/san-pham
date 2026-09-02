namespace Magic.Capture.Core.Imaging;

public sealed record ImageDifferenceOptions(
    int Threshold = 8,
    bool IgnoreAlpha = true,
    bool IgnoreFullyTransparent = false)
{
    public ImageDifferenceOptions Normalize() => this with { Threshold = Math.Clamp(Threshold, 0, 255) };
}

public sealed record ImageDifferenceStatistics(
    long PixelCount,
    long ComparedPixelCount,
    long ChangedPixelCount,
    double ChangedPixelPercent,
    double MeanAbsoluteDifference,
    double MeanBlueDifference,
    double MeanGreenDifference,
    double MeanRedDifference,
    double MeanAlphaDifference);

public static class ImageDifference
{
    public static ImageDifferenceStatistics AnalyzeBgra(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ImageDifferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (first.Length == 0 || first.Length != second.Length || (first.Length & 3) != 0)
            throw new ArgumentException("BGRA buffers must be non-empty, equal length, and contain complete pixels.");

        options = (options ?? new ImageDifferenceOptions()).Normalize();
        cancellationToken.ThrowIfCancellationRequested();
        long compared = 0;
        long changed = 0;
        long sumB = 0;
        long sumG = 0;
        long sumR = 0;
        long sumA = 0;

        for (var index = 0; index < first.Length; index += 4)
        {
            if ((index & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (options.IgnoreFullyTransparent && first[index + 3] == 0 && second[index + 3] == 0)
                continue;

            var db = Math.Abs(first[index] - second[index]);
            var dg = Math.Abs(first[index + 1] - second[index + 1]);
            var dr = Math.Abs(first[index + 2] - second[index + 2]);
            var da = Math.Abs(first[index + 3] - second[index + 3]);
            var max = Math.Max(db, Math.Max(dg, dr));
            if (!options.IgnoreAlpha) max = Math.Max(max, da);
            if (max > options.Threshold) changed++;
            compared++;
            sumB += db;
            sumG += dg;
            sumR += dr;
            sumA += da;
        }

        var pixelCount = first.Length / 4L;
        if (compared == 0)
            return new ImageDifferenceStatistics(pixelCount, 0, 0, 0, 0, 0, 0, 0, 0);

        var meanB = sumB / (double)compared;
        var meanG = sumG / (double)compared;
        var meanR = sumR / (double)compared;
        var meanA = sumA / (double)compared;
        var channelCount = options.IgnoreAlpha ? 3d : 4d;
        var mean = options.IgnoreAlpha
            ? (sumB + sumG + sumR) / (compared * channelCount)
            : (sumB + sumG + sumR + sumA) / (compared * channelCount);
        return new ImageDifferenceStatistics(
            pixelCount,
            compared,
            changed,
            changed * 100d / compared,
            mean,
            meanB,
            meanG,
            meanR,
            meanA);
    }
}
