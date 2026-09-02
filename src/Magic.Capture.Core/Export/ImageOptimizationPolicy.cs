namespace Magic.Capture.Core.Export;

public sealed record ImageOptimizationPolicy(
    long TargetBytes = 1_000_000,
    int JpegQuality = 90,
    int MinimumJpegQuality = 25,
    int MaxDimension = 16384)
{
    public ImageOptimizationPolicy Normalize()
    {
        var quality = Math.Clamp(JpegQuality, 1, 100);
        return this with
        {
            TargetBytes = Math.Clamp(TargetBytes, 16L * 1024, 256L * 1024 * 1024),
            JpegQuality = quality,
            MinimumJpegQuality = Math.Clamp(MinimumJpegQuality, 1, quality),
            MaxDimension = Math.Clamp(MaxDimension, 64, 32768)
        };
    }

    public static double ResizeScale(long currentBytes, long targetBytes)
    {
        if (currentBytes <= 0 || targetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(currentBytes));
        if (currentBytes <= targetBytes) return 1.0;
        // File size is roughly proportional to pixel count at a fixed codec/quality, so the
        // square root gives a useful first resize estimate. Clamp keeps a single step bounded.
        return Math.Clamp(Math.Sqrt(targetBytes / (double)currentBytes), 0.35, 0.95);
    }
}
