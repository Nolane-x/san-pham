namespace Magic.Capture.Core.Imaging;

public sealed record ImageComparisonMetricResult(
    double MeanSquaredError,
    double PeakSignalToNoiseRatio,
    double StructuralSimilarity);

public static class ImageComparisonMetrics
{
    public static ImageComparisonMetricResult Calculate(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, CancellationToken cancellationToken = default) =>
        CalculateCore(first, second, 1, 1, cancellationToken);

    /// <summary>Calculates metrics over B/G/R bytes only without allocating an RGB copy.</summary>
    public static ImageComparisonMetricResult CalculateBgra(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, CancellationToken cancellationToken = default)
    {
        if ((first.Length & 3) != 0 || (second.Length & 3) != 0)
            throw new ArgumentException("BGRA buffers must contain complete 4-byte pixels.");
        return CalculateCore(first, second, 4, 3, cancellationToken);
    }

    private static ImageComparisonMetricResult CalculateCore(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        int stride,
        int channels,
        CancellationToken cancellationToken)
    {
        if (first.Length == 0 || first.Length != second.Length)
            throw new ArgumentException("Image buffers must be non-empty and have equal length.");

        cancellationToken.ThrowIfCancellationRequested();
        var sampleCount = checked(first.Length / stride * channels);
        double sumSquaredError = 0;
        double sumX = 0;
        double sumY = 0;
        for (var offset = 0; offset < first.Length; offset += stride)
        {
            if ((offset & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var channel = 0; channel < channels; channel++)
            {
                var x = first[offset + channel];
                var y = second[offset + channel];
                var delta = x - y;
                sumSquaredError += delta * delta;
                sumX += x;
                sumY += y;
            }
        }

        var meanX = sumX / sampleCount;
        var meanY = sumY / sampleCount;
        double varianceX = 0;
        double varianceY = 0;
        double covariance = 0;
        for (var offset = 0; offset < first.Length; offset += stride)
        {
            if ((offset & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var channel = 0; channel < channels; channel++)
            {
                var dx = first[offset + channel] - meanX;
                var dy = second[offset + channel] - meanY;
                varianceX += dx * dx;
                varianceY += dy * dy;
                covariance += dx * dy;
            }
        }
        varianceX /= sampleCount;
        varianceY /= sampleCount;
        covariance /= sampleCount;

        var mse = sumSquaredError / sampleCount;
        var psnr = mse == 0 ? double.PositiveInfinity : 10d * Math.Log10(255d * 255d / mse);
        const double c1 = 6.5025;   // (0.01 * 255)^2
        const double c2 = 58.5225;  // (0.03 * 255)^2
        var numerator = (2 * meanX * meanY + c1) * (2 * covariance + c2);
        var denominator = (meanX * meanX + meanY * meanY + c1) * (varianceX + varianceY + c2);
        var ssim = denominator == 0 ? 1 : Math.Clamp(numerator / denominator, -1, 1);
        return new ImageComparisonMetricResult(mse, psnr, ssim);
    }
}
