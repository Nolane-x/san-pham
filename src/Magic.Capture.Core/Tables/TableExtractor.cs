using System.Text;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;

namespace Magic.Capture.Core.Tables;

public static class TableExtractor
{
    public const int MaximumInputWords = OcrSpatialIndex.MaximumWords;
    public const int MaximumOutputRows = OcrSpatialIndex.MaximumLines;
    public const int MaximumOutputColumns = 512;
    public const int MaximumCellCharacters = 4_096;

    private sealed record Cell(string Text, PixelRect Bounds)
    {
        public double CenterX => Bounds.X + Bounds.Width / 2d;
    }

    private sealed class RowCluster
    {
        private double _centerYTotal;
        public List<OcrWord> Words { get; } = [];
        public double CenterY => Words.Count == 0 ? 0 : _centerYTotal / Words.Count;
        public PixelRect Bounds { get; private set; } = PixelRect.Empty;

        public void Add(OcrWord word)
        {
            Words.Add(word);
            _centerYTotal += word.Bounds.Y + word.Bounds.Height / 2d;
            Bounds = PixelRect.Union(Bounds, word.Bounds);
        }
    }

    public static DetectedTable? TryExtract(OcrDocument document, TableExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new TableExtractionOptions();

        // Take before materialization so malformed/imported OCR cannot create an unbounded working set.
        var words = document.Lines
            .Take(OcrSpatialIndex.MaximumLines)
            .SelectMany(line => line.Words)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text) && !word.Bounds.IsEmpty)
            .Take(MaximumInputWords)
            .ToArray();

        if (words.Length < 4)
            return null;

        var medianHeight = Median(words.Select(word => (double)word.Bounds.Height));
        if (medianHeight <= 0)
            return null;

        var rows = ClusterRows(words, medianHeight, options.RowCenterToleranceFactor)
            .Take(MaximumOutputRows)
            .Select(row => MergeIntoCells(row, medianHeight, options))
            .Where(cells => cells.Count > 0)
            .ToArray();

        var candidateRows = rows.Where(row => row.Count >= 2).ToArray();
        if (candidateRows.Length < 2)
            return null;

        var columnCount = candidateRows
            .GroupBy(row => row.Count)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .Select(group => group.Key)
            .First();

        if (columnCount is < 2 or > MaximumOutputColumns)
            return null;

        var reference = candidateRows
            .Where(row => row.Count == columnCount)
            .OrderBy(row => row[0].Bounds.Y)
            .First();
        var anchors = reference.Select(cell => cell.CenterX).ToArray();

        var renderedRows = new List<IReadOnlyList<string>>(Math.Min(candidateRows.Length, MaximumOutputRows));
        var assigned = 0;
        var supported = 0;
        var assignmentTolerance = Math.Max(medianHeight * options.ColumnAssignmentToleranceFactor, 24d);

        foreach (var row in candidateRows.Take(MaximumOutputRows))
        {
            var values = Enumerable.Repeat(string.Empty, columnCount).ToArray();
            foreach (var cell in row)
            {
                var nearestIndex = 0;
                var nearestDistance = double.MaxValue;
                for (var index = 0; index < anchors.Length; index++)
                {
                    var distance = Math.Abs(cell.CenterX - anchors[index]);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = index;
                    }
                }

                values[nearestIndex] = MergeCellText(values[nearestIndex], cell.Text);
                assigned++;
                if (nearestDistance <= assignmentTolerance)
                    supported++;
            }
            renderedRows.Add(values);
        }

        if (renderedRows.Count < 2)
            return null;

        var rowConsistency = candidateRows.Count(row => row.Count == columnCount) / (double)candidateRows.Length;
        var columnSupport = assigned == 0 ? 0 : supported / (double)assigned;
        var nonEmptyCells = renderedRows.Sum(row => row.Count(value => !string.IsNullOrWhiteSpace(value)));
        var density = nonEmptyCells / (double)(renderedRows.Count * columnCount);
        var confidence = Math.Clamp(rowConsistency * 0.45 + columnSupport * 0.40 + density * 0.15, 0, 1);

        if (confidence < options.MinimumConfidence)
            return null;

        var tableBounds = candidateRows
            .SelectMany(row => row)
            .Select(cell => cell.Bounds)
            .Aggregate(PixelRect.Empty, PixelRect.Union);

        return new DetectedTable(renderedRows, columnCount, renderedRows.Count, confidence, tableBounds);
    }

    private static IReadOnlyList<RowCluster> ClusterRows(IEnumerable<OcrWord> words, double medianHeight, double toleranceFactor)
    {
        var rows = new List<RowCluster>();
        var tolerance = Math.Max(3d, medianHeight * toleranceFactor);

        foreach (var word in words.OrderBy(w => w.Bounds.Y + w.Bounds.Height / 2d).ThenBy(w => w.Bounds.X))
        {
            var centerY = word.Bounds.Y + word.Bounds.Height / 2d;
            RowCluster? bestRow = null;
            var bestDistance = double.MaxValue;

            // Words arrive top-to-bottom. Search from the newest row backwards and stop once
            // earlier rows are safely outside the row-center tolerance and cannot overlap.
            for (var index = rows.Count - 1; index >= 0; index--)
            {
                var row = rows[index];
                var distance = Math.Abs(row.CenterY - centerY);
                var overlaps = VerticalOverlapRatio(row.Bounds, word.Bounds) >= 0.35;
                if (distance <= tolerance || overlaps)
                {
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestRow = row;
                    }
                    continue;
                }

                if (row.CenterY < centerY - tolerance * 2d)
                    break;
            }

            if (bestRow is null)
            {
                if (rows.Count >= MaximumOutputRows)
                    continue;
                bestRow = new RowCluster();
                rows.Add(bestRow);
            }
            bestRow.Add(word);
        }

        return rows;
    }

    private static IReadOnlyList<Cell> MergeIntoCells(RowCluster row, double medianHeight, TableExtractionOptions options)
    {
        var sorted = row.Words.OrderBy(word => word.Bounds.X).ToArray();
        if (sorted.Length == 0)
            return [];

        var threshold = Math.Max(options.MinimumCellGapPx, medianHeight * options.CellGapFactor);
        var cells = new List<Cell>(Math.Min(sorted.Length, MaximumOutputColumns + 1));
        var currentText = BoundCellText(sorted[0].Text);
        var currentBounds = sorted[0].Bounds;

        for (var i = 1; i < sorted.Length; i++)
        {
            var next = sorted[i];
            var gap = next.Bounds.X - currentBounds.Right;
            if (gap <= threshold)
            {
                currentText = MergeCellText(currentText, next.Text);
                currentBounds = PixelRect.Union(currentBounds, next.Bounds);
            }
            else
            {
                cells.Add(new Cell(currentText, currentBounds));
                if (cells.Count > MaximumOutputColumns)
                    return cells;
                currentText = BoundCellText(next.Text);
                currentBounds = next.Bounds;
            }
        }

        cells.Add(new Cell(currentText, currentBounds));
        return cells;
    }

    private static string MergeCellText(string existing, string? next)
    {
        var nextText = BoundCellText(next);
        if (existing.Length == 0) return nextText;
        if (nextText.Length == 0) return existing;
        if (existing.Length >= MaximumCellCharacters) return existing;

        var remaining = MaximumCellCharacters - existing.Length;
        if (remaining <= 1) return existing;
        var take = Math.Min(nextText.Length, remaining - 1);
        return existing + " " + nextText[..take];
    }

    private static string BoundCellText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        return text.Length <= MaximumCellCharacters ? text : text[..MaximumCellCharacters];
    }

    private static double VerticalOverlapRatio(PixelRect a, PixelRect b)
    {
        var overlap = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
        var minHeight = Math.Min(a.Height, b.Height);
        return minHeight <= 0 ? 0 : overlap / (double)minHeight;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Take(MaximumInputWords).Order().ToArray();
        if (sorted.Length == 0) return 0;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2d
            : sorted[middle];
    }
}
