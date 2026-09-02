namespace Magic.Capture.Core.VideoEditing;

public sealed record VideoEditTrackingResult(
    VideoEditCrop Bounds,
    double MeanAbsoluteError,
    bool IsConfident);

public static class VideoEditTemplateTracker
{
    public const double DefaultConfidenceErrorThreshold = 38.0;

    public static VideoEditTrackingResult TrackNext(
        ReadOnlySpan<byte> previousBgra,
        ReadOnlySpan<byte> currentBgra,
        int width,
        int height,
        VideoEditCrop previousBounds,
        int searchRadiusPixels = 32,
        int sampleStep = 3,
        double confidenceErrorThreshold = DefaultConfidenceErrorThreshold)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var expected = checked(width * height * 4);
        if (previousBgra.Length != expected || currentBgra.Length != expected)
            throw new ArgumentException("Tracking frames must be tightly packed BGRA buffers with identical dimensions.");
        if (searchRadiusPixels < 0 || searchRadiusPixels > 256) throw new ArgumentOutOfRangeException(nameof(searchRadiusPixels));
        if (sampleStep < 1 || sampleStep > 16) throw new ArgumentOutOfRangeException(nameof(sampleStep));
        if (!double.IsFinite(confidenceErrorThreshold) || confidenceErrorThreshold < 0 || confidenceErrorThreshold > 255)
            throw new ArgumentOutOfRangeException(nameof(confidenceErrorThreshold));

        var normalized = VideoEditRules.NormalizeCrop(previousBounds);
        var template = ToPixelRect(normalized, width, height);
        var minX = Math.Max(0, template.X - searchRadiusPixels);
        var maxX = Math.Min(width - template.Width, template.X + searchRadiusPixels);
        var minY = Math.Max(0, template.Y - searchRadiusPixels);
        var maxY = Math.Min(height - template.Height, template.Y + searchRadiusPixels);

        var bestX = template.X;
        var bestY = template.Y;
        var bestError = double.PositiveInfinity;
        for (var candidateY = minY; candidateY <= maxY; candidateY++)
        {
            for (var candidateX = minX; candidateX <= maxX; candidateX++)
            {
                var error = MeanAbsoluteLumaError(previousBgra, currentBgra, width, template, candidateX, candidateY, sampleStep);
                if (error >= bestError) continue;
                bestError = error;
                bestX = candidateX;
                bestY = candidateY;
            }
        }

        var bounds = new VideoEditCrop(
            (double)bestX / width,
            (double)bestY / height,
            (double)template.Width / width,
            (double)template.Height / height);
        return new VideoEditTrackingResult(bounds, bestError, bestError <= confidenceErrorThreshold);
    }

    private static PixelRect ToPixelRect(VideoEditCrop crop, int width, int height)
    {
        var x = Math.Clamp((int)Math.Round(crop.X * width), 0, width - 1);
        var y = Math.Clamp((int)Math.Round(crop.Y * height), 0, height - 1);
        var w = Math.Clamp((int)Math.Round(crop.Width * width), 1, width - x);
        var h = Math.Clamp((int)Math.Round(crop.Height * height), 1, height - y);
        return new PixelRect(x, y, w, h);
    }

    private static double MeanAbsoluteLumaError(
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current,
        int width,
        PixelRect template,
        int candidateX,
        int candidateY,
        int sampleStep)
    {
        long error = 0;
        var count = 0;
        for (var dy = 0; dy < template.Height; dy += sampleStep)
        {
            for (var dx = 0; dx < template.Width; dx += sampleStep)
            {
                var previousOffset = checked(((template.Y + dy) * width + template.X + dx) * 4);
                var currentOffset = checked(((candidateY + dy) * width + candidateX + dx) * 4);
                error += Math.Abs(Luma(previous, previousOffset) - Luma(current, currentOffset));
                count++;
            }
        }
        return count == 0 ? 255.0 : (double)error / count;
    }

    private static int Luma(ReadOnlySpan<byte> bgra, int offset) =>
        (bgra[offset] * 29 + bgra[offset + 1] * 150 + bgra[offset + 2] * 77 + 128) >> 8;

    private readonly record struct PixelRect(int X, int Y, int Width, int Height);
}
