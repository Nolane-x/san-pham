using System.Text;

namespace Magic.Capture.Core.Tables;

public readonly record struct TableCellRange(int StartRow, int StartColumn, int EndRow, int EndColumn)
{
    public int RowCount => EndRow - StartRow + 1;
    public int ColumnCount => EndColumn - StartColumn + 1;
    public long CellCount => (long)Math.Max(0, RowCount) * Math.Max(0, ColumnCount);

    public static TableCellRange Normalize(int rowA, int columnA, int rowB, int columnB) =>
        new(Math.Min(rowA, rowB), Math.Min(columnA, columnB), Math.Max(rowA, rowB), Math.Max(columnA, columnB));

    public bool Contains(int row, int column) =>
        row >= StartRow && row <= EndRow && column >= StartColumn && column <= EndColumn;

    public bool Overlaps(TableCellRange other) =>
        StartRow <= other.EndRow && EndRow >= other.StartRow &&
        StartColumn <= other.EndColumn && EndColumn >= other.StartColumn;
}

public sealed record TableMergeRegion(TableCellRange Range)
{
    public int AnchorRow => Range.StartRow;
    public int AnchorColumn => Range.StartColumn;
}

public sealed class EditableTableDocument
{
    public const int MaximumRows = 2_048;
    public const int MaximumColumns = 128;
    public const int MaximumCells = 100_000;
    public const int MaximumCellCharacters = 4_096;
    public const int MaximumTotalCharacters = 2_000_000;
    public const int MaximumMerges = 2_048;

    private readonly string[][] _rows;
    private readonly IReadOnlyList<string>[] _rowViews;
    private readonly IReadOnlyList<IReadOnlyList<string>> _rowViewsView;
    private readonly IReadOnlyList<TableMergeRegion> _merges;
    private readonly IReadOnlyDictionary<long, TableMergeRegion> _mergeLookup;
    private readonly long _totalCharacters;

    private EditableTableDocument(
        string[][] rows,
        int columnCount,
        IReadOnlyList<TableMergeRegion> merges,
        long totalCharacters,
        IReadOnlyList<string>[]? rowViews = null)
    {
        _rows = rows;
        ColumnCount = columnCount;
        _merges = merges;
        _totalCharacters = totalCharacters;
        _rowViews = rowViews ?? rows.Select(row => (IReadOnlyList<string>)Array.AsReadOnly(row)).ToArray();
        _rowViewsView = Array.AsReadOnly(_rowViews);
        _mergeLookup = BuildMergeLookup(merges);
    }

    public IReadOnlyList<IReadOnlyList<string>> Rows => _rowViewsView;
    public IReadOnlyList<TableMergeRegion> Merges => _merges;
    public int RowCount => _rows.Length;
    public int ColumnCount { get; }

    public static EditableTableDocument FromDetectedTable(DetectedTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Create(table.Rows, table.ColumnCount);
    }

    public static EditableTableDocument Create(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int columnCount,
        IReadOnlyList<TableMergeRegion>? merges = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ValidateShape(rows.Count, columnCount);

        var normalizedRows = new string[rows.Count][];
        long totalCharacters = 0;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var source = rows[rowIndex] ?? Array.Empty<string>();
            if (source.Count > columnCount)
                throw new InvalidOperationException("A table row contains more cells than the declared column count.");
            var row = new string[columnCount];
            for (var column = 0; column < columnCount; column++)
            {
                var value = column < source.Count ? source[column] ?? string.Empty : string.Empty;
                ValidateCellText(value);
                totalCharacters += value.Length;
                if (totalCharacters > MaximumTotalCharacters)
                    throw new InvalidOperationException($"Editable table text exceeds {MaximumTotalCharacters:N0} characters.");
                row[column] = value;
            }
            normalizedRows[rowIndex] = row;
        }

        var normalizedMerges = NormalizeMerges(merges ?? [], rows.Count, columnCount);
        return new EditableTableDocument(normalizedRows, columnCount, normalizedMerges, totalCharacters);
    }

    public string CellAt(int row, int column)
    {
        ValidateCell(row, column);
        return _rows[row][column];
    }

    public string DisplayCellAt(int row, int column)
    {
        ValidateCell(row, column);
        var merge = MergeAt(row, column);
        return merge is null || (merge.AnchorRow == row && merge.AnchorColumn == column)
            ? _rows[row][column]
            : string.Empty;
    }

    public TableMergeRegion? MergeAt(int row, int column)
    {
        ValidateCell(row, column);
        return _mergeLookup.TryGetValue(CellKey(row, column), out var merge) ? merge : null;
    }

    internal EditableTableDocument WithCellValue(int row, int column, string value)
    {
        ValidateCell(row, column);
        ValidateCellText(value);
        var newTotal = checked(_totalCharacters - _rows[row][column].Length + value.Length);
        if (newTotal > MaximumTotalCharacters)
            throw new InvalidOperationException($"Editable table text exceeds {MaximumTotalCharacters:N0} characters.");
        var rows = (string[][])_rows.Clone();
        rows[row] = (string[])_rows[row].Clone();
        rows[row][column] = value;
        var rowViews = (IReadOnlyList<string>[])_rowViews.Clone();
        rowViews[row] = Array.AsReadOnly(rows[row]);
        return new EditableTableDocument(rows, ColumnCount, _merges, newTotal, rowViews);
    }

    internal EditableTableDocument WithMerges(IReadOnlyList<TableMergeRegion> merges)
    {
        var normalized = NormalizeMerges(merges, RowCount, ColumnCount);
        return new EditableTableDocument(_rows, ColumnCount, normalized, _totalCharacters, _rowViews);
    }

    private void ValidateCell(int row, int column)
    {
        if ((uint)row >= (uint)RowCount || (uint)column >= (uint)ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(row), $"Cell R{row + 1}C{column + 1} is outside the table.");
    }

    private static void ValidateShape(int rowCount, int columnCount)
    {
        if (columnCount < 0 || columnCount > MaximumColumns)
            throw new InvalidOperationException($"Editable table supports at most {MaximumColumns:N0} columns.");
        if (rowCount > MaximumRows)
            throw new InvalidOperationException($"Editable table supports at most {MaximumRows:N0} rows.");
        if ((long)rowCount * columnCount > MaximumCells)
            throw new InvalidOperationException($"Editable table supports at most {MaximumCells:N0} cells.");
    }

    private static void ValidateCellText(string value)
    {
        if (value.Length > MaximumCellCharacters)
            throw new InvalidOperationException($"A table cell exceeds {MaximumCellCharacters:N0} characters.");
    }

    private static IReadOnlyList<TableMergeRegion> NormalizeMerges(IReadOnlyList<TableMergeRegion> merges, int rows, int columns)
    {
        if (merges.Count > MaximumMerges)
            throw new InvalidOperationException($"Editable table supports at most {MaximumMerges:N0} merge regions.");
        var result = new List<TableMergeRegion>(merges.Count);
        var occupied = new HashSet<long>();
        foreach (var merge in merges.OrderBy(item => item.Range.StartRow).ThenBy(item => item.Range.StartColumn))
        {
            var range = merge.Range;
            if (range.StartRow < 0 || range.StartColumn < 0 || range.EndRow >= rows || range.EndColumn >= columns || range.StartRow > range.EndRow || range.StartColumn > range.EndColumn)
                throw new InvalidOperationException("A merge range is outside the editable table.");
            if (range.CellCount <= 1) continue;
            for (var row = range.StartRow; row <= range.EndRow; row++)
            {
                for (var column = range.StartColumn; column <= range.EndColumn; column++)
                {
                    if (!occupied.Add(CellKey(row, column)))
                        throw new InvalidOperationException("Merge ranges cannot overlap.");
                }
            }
            result.Add(new TableMergeRegion(range));
        }
        return result;
    }

    private static IReadOnlyDictionary<long, TableMergeRegion> BuildMergeLookup(IReadOnlyList<TableMergeRegion> merges)
    {
        var lookup = new Dictionary<long, TableMergeRegion>();
        foreach (var merge in merges)
            for (var row = merge.Range.StartRow; row <= merge.Range.EndRow; row++)
                for (var column = merge.Range.StartColumn; column <= merge.Range.EndColumn; column++)
                    lookup[CellKey(row, column)] = merge;
        return lookup;
    }

    private static long CellKey(int row, int column) => ((long)row << 32) | (uint)column;
}

public static class TableDocumentOperations
{
    public const int MaximumCopiedCharacters = 2_500_000;
    public static EditableTableDocument SetCell(EditableTableDocument document, int row, int column, string? value)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = document.CellAt(row, column);
        value ??= string.Empty;
        if (value.Length > EditableTableDocument.MaximumCellCharacters)
            throw new InvalidOperationException($"A table cell exceeds {EditableTableDocument.MaximumCellCharacters:N0} characters.");
        return document.WithCellValue(row, column, value);
    }

    public static EditableTableDocument InsertRow(EditableTableDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (index < 0 || index > document.RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (document.RowCount >= EditableTableDocument.MaximumRows || (long)(document.RowCount + 1) * document.ColumnCount > EditableTableDocument.MaximumCells)
            throw new InvalidOperationException("Editable table row limit reached.");
        var rows = new List<IReadOnlyList<string>>(document.RowCount + 1);
        for (var row = 0; row < document.RowCount + 1; row++)
            rows.Add(row == index ? new string[document.ColumnCount] : document.Rows[row < index ? row : row - 1]);
        return EditableTableDocument.Create(rows, document.ColumnCount, TransformRowsForInsert(document.Merges, index));
    }

    public static EditableTableDocument DeleteRow(EditableTableDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        if ((uint)index >= (uint)document.RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        var rows = document.Rows.Where((_, row) => row != index).ToArray();
        return EditableTableDocument.Create(rows, document.ColumnCount, TransformRowsForDelete(document.Merges, index));
    }

    public static EditableTableDocument InsertColumn(EditableTableDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (index < 0 || index > document.ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (document.ColumnCount >= EditableTableDocument.MaximumColumns || (long)document.RowCount * (document.ColumnCount + 1) > EditableTableDocument.MaximumCells)
            throw new InvalidOperationException("Editable table column limit reached.");
        var rows = new IReadOnlyList<string>[document.RowCount];
        for (var row = 0; row < document.RowCount; row++)
        {
            var values = new string[document.ColumnCount + 1];
            for (var column = 0; column < values.Length; column++)
                values[column] = column == index ? string.Empty : document.CellAt(row, column < index ? column : column - 1);
            rows[row] = values;
        }
        return EditableTableDocument.Create(rows, document.ColumnCount + 1, TransformColumnsForInsert(document.Merges, index));
    }

    public static EditableTableDocument DeleteColumn(EditableTableDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        if ((uint)index >= (uint)document.ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        var rows = new IReadOnlyList<string>[document.RowCount];
        for (var row = 0; row < document.RowCount; row++)
            rows[row] = Enumerable.Range(0, document.ColumnCount).Where(column => column != index).Select(column => document.CellAt(row, column)).ToArray();
        return EditableTableDocument.Create(rows, document.ColumnCount - 1, TransformColumnsForDelete(document.Merges, index));
    }

    public static EditableTableDocument Merge(EditableTableDocument document, TableCellRange range)
    {
        ArgumentNullException.ThrowIfNull(document);
        range = NormalizeAndValidateRange(document, range);
        if (range.CellCount <= 1) throw new InvalidOperationException("Select at least two cells to merge.");
        if (document.Merges.Any(existing => existing.Range.Overlaps(range)))
            throw new InvalidOperationException("The selection overlaps an existing merged range.");
        return document.WithMerges(document.Merges.Append(new TableMergeRegion(range)).ToArray());
    }

    public static EditableTableDocument Unmerge(EditableTableDocument document, TableCellRange range)
    {
        ArgumentNullException.ThrowIfNull(document);
        range = NormalizeAndValidateRange(document, range);
        return document.WithMerges(document.Merges.Where(merge => !merge.Range.Overlaps(range)).ToArray());
    }

    public static string CopySelectionTsv(EditableTableDocument document, TableCellRange range)
    {
        ArgumentNullException.ThrowIfNull(document);
        range = NormalizeAndValidateRange(document, range);
        var builder = new StringBuilder(Math.Min(MaximumCopiedCharacters, 16_384));
        var projectedLength = 0;
        for (var row = range.StartRow; row <= range.EndRow; row++)
        {
            if (row > range.StartRow)
            {
                EnsureCopyCapacity(ref projectedLength, 2);
                builder.Append("\r\n");
            }
            for (var column = range.StartColumn; column <= range.EndColumn; column++)
            {
                if (column > range.StartColumn)
                {
                    EnsureCopyCapacity(ref projectedLength, 1);
                    builder.Append('\t');
                }
                var value = document.DisplayCellAt(row, column);
                EnsureCopyCapacity(ref projectedLength, DelimitedEncodedLength(value));
                AppendDelimited(builder, value);
            }
        }
        return builder.ToString();
    }

    public static TableCellRange NormalizeAndValidateRange(EditableTableDocument document, TableCellRange range)
    {
        var normalized = TableCellRange.Normalize(range.StartRow, range.StartColumn, range.EndRow, range.EndColumn);
        if (normalized.StartRow < 0 || normalized.StartColumn < 0 || normalized.EndRow >= document.RowCount || normalized.EndColumn >= document.ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(range), "Selection is outside the table.");
        if (normalized.CellCount > EditableTableDocument.MaximumCells)
            throw new InvalidOperationException("Selection exceeds the editable table cell budget.");
        return normalized;
    }

    private static IReadOnlyList<TableMergeRegion> TransformRowsForInsert(IReadOnlyList<TableMergeRegion> merges, int index) =>
        merges.Select(merge =>
        {
            var r = merge.Range;
            if (index <= r.StartRow) r = r with { StartRow = r.StartRow + 1, EndRow = r.EndRow + 1 };
            else if (index <= r.EndRow) r = r with { EndRow = r.EndRow + 1 };
            return new TableMergeRegion(r);
        }).ToArray();

    private static IReadOnlyList<TableMergeRegion> TransformRowsForDelete(IReadOnlyList<TableMergeRegion> merges, int index)
    {
        var result = new List<TableMergeRegion>();
        foreach (var merge in merges)
        {
            var r = merge.Range;
            if (index < r.StartRow) r = r with { StartRow = r.StartRow - 1, EndRow = r.EndRow - 1 };
            else if (index <= r.EndRow) r = r with { EndRow = r.EndRow - 1 };
            if (r.CellCount > 1 && r.StartRow <= r.EndRow) result.Add(new TableMergeRegion(r));
        }
        return result;
    }

    private static IReadOnlyList<TableMergeRegion> TransformColumnsForInsert(IReadOnlyList<TableMergeRegion> merges, int index) =>
        merges.Select(merge =>
        {
            var r = merge.Range;
            if (index <= r.StartColumn) r = r with { StartColumn = r.StartColumn + 1, EndColumn = r.EndColumn + 1 };
            else if (index <= r.EndColumn) r = r with { EndColumn = r.EndColumn + 1 };
            return new TableMergeRegion(r);
        }).ToArray();

    private static IReadOnlyList<TableMergeRegion> TransformColumnsForDelete(IReadOnlyList<TableMergeRegion> merges, int index)
    {
        var result = new List<TableMergeRegion>();
        foreach (var merge in merges)
        {
            var r = merge.Range;
            if (index < r.StartColumn) r = r with { StartColumn = r.StartColumn - 1, EndColumn = r.EndColumn - 1 };
            else if (index <= r.EndColumn) r = r with { EndColumn = r.EndColumn - 1 };
            if (r.CellCount > 1 && r.StartColumn <= r.EndColumn) result.Add(new TableMergeRegion(r));
        }
        return result;
    }

    private static void EnsureCopyCapacity(ref int projectedLength, int additionalCharacters)
    {
        if (additionalCharacters < 0 || projectedLength > MaximumCopiedCharacters - additionalCharacters)
            throw new InvalidOperationException($"Copied table selection exceeds the supported {MaximumCopiedCharacters:N0} character limit.");
        projectedLength += additionalCharacters;
    }

    private static int DelimitedEncodedLength(string value)
    {
        var quoted = value.IndexOfAny(['\t', '\r', '\n', '\"']) >= 0;
        if (!quoted) return value.Length;
        var quotes = 0;
        foreach (var ch in value) if (ch == '\"') quotes++;
        return checked(value.Length + quotes + 2);
    }

    private static void AppendDelimited(StringBuilder builder, string value)
    {
        if (value.IndexOfAny(['\t', '\r', '\n', '"']) < 0)
        {
            builder.Append(value);
            return;
        }
        builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
    }
}
