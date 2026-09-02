namespace Magic.Capture.Core.Imaging;

public sealed record StableEdgeBandOptions(
    double MaximumBandRatio = 0.25,
    int MinimumBandRows = 12,
    double MaximumRowChangedPercent = 4.0,
    double MinimumGlobalChangedPercent = 10.0,
    int SampleEveryColumns = 4,
    byte ChannelThreshold = 8);

public sealed record StableEdgeBands(int TopRows, int BottomRows)
{
    public static StableEdgeBands None { get; } = new(0, 0);
}

/// <summary>
/// Detects fixed top/bottom bands between two same-sized BGRA frames. It is intentionally
/// conservative: an edge is considered sticky only when the body changed materially and a
/// contiguous band from that edge remains almost unchanged.
/// </summary>
public static class StableEdgeBandDetector
{
    public static StableEdgeBands Detect(
        ReadOnlySpan<byte> firstBgra,
        ReadOnlySpan<byte> secondBgra,
        int width,
        int height,
        StableEdgeBandOptions? options = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var requiredLength = checked(width * height * 4);
        if (firstBgra.Length != requiredLength || secondBgra.Length != requiredLength)
            throw new ArgumentException("BGRA buffers must exactly match the supplied image geometry.");

        options ??= new StableEdgeBandOptions();
        var maximumBandRatio = Math.Clamp(options.MaximumBandRatio, 0.01, 0.45);
        var minimumRows = Math.Clamp(options.MinimumBandRows, 1, Math.Max(1, height / 2));
        var rowChangedLimit = Math.Clamp(options.MaximumRowChangedPercent, 0, 100);
        var minimumGlobalChanged = Math.Clamp(options.MinimumGlobalChangedPercent, 0, 100);
        var columnStep = Math.Clamp(options.SampleEveryColumns, 1, Math.Max(1, width));

        var globalChanged = FrameDifference.SampledChangedPercent(
            firstBgra, secondBgra,
            sampleEveryPixels: Math.Max(1, columnStep),
            channelThreshold: options.ChannelThreshold);
        if (globalChanged < minimumGlobalChanged) return StableEdgeBands.None;

        var maxRows = Math.Max(minimumRows, (int)Math.Floor(height * maximumBandRatio));
        maxRows = Math.Min(maxRows, Math.Max(0, height / 2 - 1));
        if (maxRows < minimumRows) return StableEdgeBands.None;

        var top = CountStableRows(firstBgra, secondBgra, width, height, fromTop: true, maxRows, columnStep, options.ChannelThreshold, rowChangedLimit);
        var bottom = CountStableRows(firstBgra, secondBgra, width, height, fromTop: false, maxRows, columnStep, options.ChannelThreshold, rowChangedLimit);

        if (top < minimumRows) top = 0;
        if (bottom < minimumRows) bottom = 0;
        if (top + bottom >= height / 2)
        {
            // Ambiguous mostly-static chrome: prefer not trimming over deleting real content.
            return StableEdgeBands.None;
        }
        return new StableEdgeBands(top, bottom);
    }

    private static int CountStableRows(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        int width,
        int height,
        bool fromTop,
        int maxRows,
        int columnStep,
        byte channelThreshold,
        double changedPercentLimit)
    {
        var stableRows = 0;
        for (var index = 0; index < maxRows; index++)
        {
            var y = fromTop ? index : height - 1 - index;
            long samples = 0;
            long changed = 0;
            for (var x = 0; x < width; x += columnStep)
            {
                var offset = (y * width + x) * 4;
                var db = Math.Abs(first[offset] - second[offset]);
                var dg = Math.Abs(first[offset + 1] - second[offset + 1]);
                var dr = Math.Abs(first[offset + 2] - second[offset + 2]);
                if (Math.Max(db, Math.Max(dg, dr)) > channelThreshold) changed++;
                samples++;
            }
            var changedPercent = samples == 0 ? 100 : changed * 100d / samples;
            if (changedPercent > changedPercentLimit) break;
            stableRows++;
        }
        return stableRows;
    }
}
