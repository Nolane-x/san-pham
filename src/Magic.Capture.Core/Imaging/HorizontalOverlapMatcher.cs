namespace Magic.Capture.Core.Imaging;

public sealed record HorizontalOverlapMatch(int OverlapColumns, double MeanAbsoluteDifference);

public sealed record HorizontalOverlapOptions(
    double MinimumOverlapRatio = 0.12,
    double MaximumOverlapRatio = 0.75,
    double MaximumMeanAbsoluteDifference = 18.0,
    int RowSampleStep = 4,
    int ColumnSampleStep = 2);

public static class HorizontalOverlapMatcher
{
    public static HorizontalOverlapMatch? Find(
        ReadOnlySpan<byte> leftGray,
        int leftWidth,
        ReadOnlySpan<byte> rightGray,
        int rightWidth,
        int height,
        HorizontalOverlapOptions? options = null)
    {
        options ??= new HorizontalOverlapOptions();
        if (leftWidth <= 0 || rightWidth <= 0 || height <= 0) return null;
        if (leftGray.Length < checked(leftWidth * height) || rightGray.Length < checked(rightWidth * height)) return null;

        var maxPossible = Math.Min(leftWidth, rightWidth);
        var minOverlap = Math.Max(1, (int)Math.Ceiling(maxPossible * Math.Clamp(options.MinimumOverlapRatio, 0.01, 1.0)));
        var maxOverlap = Math.Max(minOverlap, (int)Math.Floor(maxPossible * Math.Clamp(options.MaximumOverlapRatio, 0.01, 1.0)));
        maxOverlap = Math.Min(maxOverlap, maxPossible);
        var rowStep = Math.Max(1, options.RowSampleStep);
        var columnStep = Math.Max(1, options.ColumnSampleStep);
        var threshold = Math.Max(0, options.MaximumMeanAbsoluteDifference);

        HorizontalOverlapMatch? best = null;
        for (var overlap = minOverlap; overlap <= maxOverlap; overlap++)
        {
            long difference = 0;
            long samples = 0;
            var leftStart = leftWidth - overlap;
            for (var y = 0; y < height; y += rowStep)
            {
                var leftRow = y * leftWidth;
                var rightRow = y * rightWidth;
                for (var x = 0; x < overlap; x += columnStep)
                {
                    difference += Math.Abs(leftGray[leftRow + leftStart + x] - rightGray[rightRow + x]);
                    samples++;
                }
            }

            if (samples == 0) continue;
            var mean = difference / (double)samples;
            if (best is null || mean < best.MeanAbsoluteDifference - 1e-9 ||
                (Math.Abs(mean - best.MeanAbsoluteDifference) < 1e-9 && overlap > best.OverlapColumns))
            {
                best = new HorizontalOverlapMatch(overlap, mean);
            }
        }

        return best is not null && best.MeanAbsoluteDifference <= threshold ? best : null;
    }
}
