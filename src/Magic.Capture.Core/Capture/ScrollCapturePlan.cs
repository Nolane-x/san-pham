namespace Magic.Capture.Core.Capture;

public enum ScrollAxis
{
    Vertical,
    Horizontal
}

public readonly record struct ScrollVector(int HorizontalWheelDelta, int VerticalWheelDelta)
{
    public static ScrollVector None => new(0, 0);

    public ScrollAxis? PrimaryAxis =>
        HorizontalWheelDelta == 0 && VerticalWheelDelta == 0 ? null :
        Math.Abs((long)HorizontalWheelDelta) >= Math.Abs((long)VerticalWheelDelta) ? ScrollAxis.Horizontal : ScrollAxis.Vertical;
}

public readonly record struct ScrollCaptureTile(int Index, int Row, int Column, ScrollVector MoveBeforeCapture);

public sealed record ScrollCaptureGridPlan(int Rows, int Columns, IReadOnlyList<ScrollCaptureTile> Tiles)
{
    public const int MaximumRows = 8;
    public const int MaximumColumns = 8;
    public const int MaximumTiles = 64;

    public static ScrollCaptureGridPlan Create(
        int rows,
        int columns,
        int horizontalWheelDelta = -720,
        int verticalWheelDelta = -720)
    {
        if (rows < 1 || rows > MaximumRows) throw new ArgumentOutOfRangeException(nameof(rows), $"Rows must be between 1 and {MaximumRows}.");
        if (columns < 1 || columns > MaximumColumns) throw new ArgumentOutOfRangeException(nameof(columns), $"Columns must be between 1 and {MaximumColumns}.");
        var tileCount = checked(rows * columns);
        if (tileCount > MaximumTiles) throw new ArgumentOutOfRangeException(nameof(rows), $"A scrolling grid may contain at most {MaximumTiles} tiles.");
        if (rows > 1 && verticalWheelDelta == 0) throw new ArgumentOutOfRangeException(nameof(verticalWheelDelta), "Vertical wheel delta must be non-zero for multi-row capture.");
        if (columns > 1 && horizontalWheelDelta == 0) throw new ArgumentOutOfRangeException(nameof(horizontalWheelDelta), "Horizontal wheel delta must be non-zero for multi-column capture.");

        var tiles = new List<ScrollCaptureTile>(tileCount);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = checked(row * columns + column);
                ScrollVector move;
                if (index == 0)
                {
                    move = ScrollVector.None;
                }
                else if (column > 0)
                {
                    move = new ScrollVector(horizontalWheelDelta, 0);
                }
                else
                {
                    var horizontalReset = checked(-horizontalWheelDelta * (columns - 1));
                    move = new ScrollVector(horizontalReset, verticalWheelDelta);
                }
                tiles.Add(new ScrollCaptureTile(index, row, column, move));
            }
        }
        return new ScrollCaptureGridPlan(rows, columns, tiles.ToArray());
    }
}
