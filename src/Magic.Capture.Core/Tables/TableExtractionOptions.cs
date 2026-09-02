namespace Magic.Capture.Core.Tables;

public sealed record TableExtractionOptions(
    double RowCenterToleranceFactor = 0.70,
    int MinimumCellGapPx = 14,
    double CellGapFactor = 0.90,
    double ColumnAssignmentToleranceFactor = 4.0,
    double MinimumConfidence = 0.52);
