namespace Magic.Capture.Core.Imaging;

public static class VerticalOverlapMatcher
{
    public static OverlapMatch? FindTrimmed(
        ReadOnlySpan<byte> upperGray,
        int upperHeight,
        ReadOnlySpan<byte> lowerGray,
        int lowerHeight,
        int width,
        int upperTopRows,
        int upperBottomRows,
        int lowerTopRows,
        int lowerBottomRows,
        VerticalOverlapOptions? options = null)
    {
        if (width <= 0 || upperHeight <= 0 || lowerHeight <= 0) return null;
        if (upperGray.Length < checked(width * upperHeight) || lowerGray.Length < checked(width * lowerHeight)) return null;

        upperTopRows = Math.Max(0, upperTopRows);
        upperBottomRows = Math.Max(0, upperBottomRows);
        lowerTopRows = Math.Max(0, lowerTopRows);
        lowerBottomRows = Math.Max(0, lowerBottomRows);
        var effectiveUpperHeight = upperHeight - upperTopRows - upperBottomRows;
        var effectiveLowerHeight = lowerHeight - lowerTopRows - lowerBottomRows;
        if (effectiveUpperHeight <= 0 || effectiveLowerHeight <= 0) return null;

        var upper = upperGray.Slice(checked(upperTopRows * width), checked(effectiveUpperHeight * width));
        var lower = lowerGray.Slice(checked(lowerTopRows * width), checked(effectiveLowerHeight * width));
        return Find(upper, effectiveUpperHeight, lower, effectiveLowerHeight, width, options);
    }

    public static OverlapMatch? Find(
        ReadOnlySpan<byte> upperGray,
        int upperHeight,
        ReadOnlySpan<byte> lowerGray,
        int lowerHeight,
        int width,
        VerticalOverlapOptions? options = null)
    {
        options ??= new VerticalOverlapOptions();
        if (width <= 0 || upperHeight <= 0 || lowerHeight <= 0) return null;
        if (upperGray.Length < width * upperHeight || lowerGray.Length < width * lowerHeight) return null;

        var maxPossible = Math.Min(upperHeight, lowerHeight);
        var minOverlap = Math.Max(1, (int)Math.Ceiling(maxPossible * options.MinimumOverlapRatio));
        var maxOverlap = Math.Max(minOverlap, (int)Math.Floor(maxPossible * options.MaximumOverlapRatio));
        maxOverlap = Math.Min(maxOverlap, maxPossible);
        var rowStep = Math.Max(1, options.RowSampleStep);
        var columnStep = Math.Max(1, options.ColumnSampleStep);

        OverlapMatch? best = null;
        for (var overlap = minOverlap; overlap <= maxOverlap; overlap++)
        {
            long difference = 0;
            long samples = 0;
            var upperStartRow = upperHeight - overlap;

            for (var row = 0; row < overlap; row += rowStep)
            {
                var upperOffset = (upperStartRow + row) * width;
                var lowerOffset = row * width;
                for (var x = 0; x < width; x += columnStep)
                {
                    difference += Math.Abs(upperGray[upperOffset + x] - lowerGray[lowerOffset + x]);
                    samples++;
                }
            }

            if (samples == 0) continue;
            var mean = difference / (double)samples;
            if (best is null || mean < best.MeanAbsoluteDifference - 1e-9 ||
                (Math.Abs(mean - best.MeanAbsoluteDifference) < 1e-9 && overlap > best.OverlapRows))
            {
                best = new OverlapMatch(overlap, mean);
            }
        }

        return best is not null && best.MeanAbsoluteDifference <= options.MaximumMeanAbsoluteDifference ? best : null;
    }
}
