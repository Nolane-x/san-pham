namespace Magic.Capture.Core.Imaging;

public sealed record OverlapMatch(int OverlapRows, double MeanAbsoluteDifference);

public sealed record VerticalOverlapOptions(
    double MinimumOverlapRatio = 0.12,
    double MaximumOverlapRatio = 0.75,
    double MaximumMeanAbsoluteDifference = 18.0,
    int RowSampleStep = 2,
    int ColumnSampleStep = 4);
