using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tables;

public sealed record DetectedTable(
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int ColumnCount,
    int RowCount,
    double Confidence,
    PixelRect Bounds);
