using System.Drawing;
using System.Drawing.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed record GridStitchResult(
    byte[] PngBytes,
    IReadOnlyList<int> HorizontalSeamOverlaps,
    IReadOnlyList<int> VerticalSeamOverlaps);

internal sealed class GridImageStitcher
{
    private readonly HorizontalImageStitcher _horizontal;
    private readonly VerticalImageStitcher _vertical;

    public GridImageStitcher(HorizontalImageStitcher horizontal, VerticalImageStitcher vertical)
    {
        _horizontal = horizontal;
        _vertical = vertical;
    }

    public GridStitchResult Stitch(IReadOnlyList<byte[]> tiles, int rows, int columns)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (rows < 1 || rows > ScrollCaptureGridPlan.MaximumRows) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 1 || columns > ScrollCaptureGridPlan.MaximumColumns) throw new ArgumentOutOfRangeException(nameof(columns));
        if (tiles.Count != checked(rows * columns)) throw new ArgumentException("Grid tile count does not match rows × columns.", nameof(tiles));
        if (tiles.Count > ScrollCaptureGridPlan.MaximumTiles) throw new ArgumentException("Grid exceeds the maximum tile count.", nameof(tiles));

        if (!PngDimensions.TryRead(tiles[0], out var tileWidth, out var tileHeight))
            throw new InvalidDataException("Grid tile 1 is not a valid PNG.");
        ImageWorkloadLimits.ValidateDimensions(tileWidth, tileHeight);
        for (var i = 1; i < tiles.Count; i++)
        {
            if (!PngDimensions.TryRead(tiles[i], out var width, out var height))
                throw new InvalidDataException($"Grid tile {i + 1} is not a valid PNG.");
            if (width != tileWidth || height != tileHeight)
                throw new InvalidOperationException("All 2D scrolling tiles must have identical physical-pixel dimensions.");
        }

        var horizontalSeams = new int[Math.Max(0, columns - 1)];
        for (var boundary = 0; boundary < horizontalSeams.Length; boundary++)
        {
            var overlaps = new List<int>(rows);
            for (var row = 0; row < rows; row++)
            {
                var leftIndex = checked(row * columns + boundary);
                var rightIndex = leftIndex + 1;
                var match = _horizontal.FindPairOverlap(tiles[leftIndex], tiles[rightIndex]);
                if (match is not null) overlaps.Add(match.OverlapColumns);
            }
            var requiredConsensus = rows / 2 + 1;
            if (overlaps.Count < requiredConsensus)
                throw new InvalidOperationException($"Horizontal 2D seam {boundary + 1} did not reach overlap consensus ({overlaps.Count}/{rows} rows matched).");
            horizontalSeams[boundary] = Median(overlaps);
        }

        var verticalSeams = new int[Math.Max(0, rows - 1)];
        for (var boundary = 0; boundary < verticalSeams.Length; boundary++)
        {
            var overlaps = new List<int>(columns);
            for (var column = 0; column < columns; column++)
            {
                var upperIndex = checked(boundary * columns + column);
                var lowerIndex = checked((boundary + 1) * columns + column);
                var match = _vertical.FindPairOverlap(tiles[upperIndex], tiles[lowerIndex]);
                if (match is not null) overlaps.Add(match.OverlapRows);
            }
            var requiredConsensus = columns / 2 + 1;
            if (overlaps.Count < requiredConsensus)
                throw new InvalidOperationException($"Vertical 2D seam {boundary + 1} did not reach overlap consensus ({overlaps.Count}/{columns} columns matched).");
            verticalSeams[boundary] = Median(overlaps);
        }

        var xOrigins = new int[columns];
        for (var column = 1; column < columns; column++)
            xOrigins[column] = checked(xOrigins[column - 1] + tileWidth - horizontalSeams[column - 1]);
        var yOrigins = new int[rows];
        for (var row = 1; row < rows; row++)
            yOrigins[row] = checked(yOrigins[row - 1] + tileHeight - verticalSeams[row - 1]);

        var outputWidth = checked(xOrigins[^1] + tileWidth);
        var outputHeight = checked(yOrigins[^1] + tileHeight);
        ImageWorkloadLimits.ValidateDimensions(outputWidth, outputHeight);

        using var output = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = checked(row * columns + column);
                    using var tile = BitmapCodec.Decode(tiles[index]);
                    graphics.DrawImageUnscaled(tile, xOrigins[column], yOrigins[row]);
                }
            }
        }

        return new GridStitchResult(BitmapCodec.EncodePng(output), horizontalSeams, verticalSeams);
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : checked((values[middle - 1] + values[middle]) / 2);
    }
}
