using System.Text;

namespace Magic.Capture.Core.Tables;

public static class DelimitedTableParser
{
    public const int MaximumInputCharacters = 2_000_000;

    public static EditableTableDocument Parse(string text, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (delimiter is '\r' or '\n' or '"') throw new ArgumentOutOfRangeException(nameof(delimiter));
        if (text.Length > MaximumInputCharacters)
            throw new InvalidDataException($"Delimited table exceeds {MaximumInputCharacters:N0} characters.");
        if (text.Length == 0) return EditableTableDocument.Create([], 0);

        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var endedWithRowBreak = false;
        var maxColumnsSeen = 0;
        long cellCount = 0;

        void CommitCell()
        {
            if (cell.Length > EditableTableDocument.MaximumCellCharacters)
                throw new InvalidDataException($"A cell exceeds {EditableTableDocument.MaximumCellCharacters:N0} characters.");
            row.Add(cell.ToString());
            cell.Clear();
            cellCount++;
            if (cellCount > EditableTableDocument.MaximumCells)
                throw new InvalidDataException($"Delimited table exceeds {EditableTableDocument.MaximumCells:N0} cells.");
            if (row.Count > EditableTableDocument.MaximumColumns)
                throw new InvalidDataException($"Delimited table exceeds {EditableTableDocument.MaximumColumns:N0} columns.");
        }

        void CommitRow()
        {
            CommitCell();
            maxColumnsSeen = Math.Max(maxColumnsSeen, row.Count);
            rows.Add(row.ToArray());
            row.Clear();
            if (rows.Count > EditableTableDocument.MaximumRows)
                throw new InvalidDataException($"Delimited table exceeds {EditableTableDocument.MaximumRows:N0} rows.");
        }

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }
                if (cell.Length > EditableTableDocument.MaximumCellCharacters)
                    throw new InvalidDataException($"A cell exceeds {EditableTableDocument.MaximumCellCharacters:N0} characters.");
                endedWithRowBreak = false;
                continue;
            }

            if (ch == '"' && cell.Length == 0)
            {
                inQuotes = true;
                endedWithRowBreak = false;
            }
            else if (ch == delimiter)
            {
                CommitCell();
                endedWithRowBreak = false;
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                CommitRow();
                endedWithRowBreak = true;
            }
            else
            {
                cell.Append(ch);
                if (cell.Length > EditableTableDocument.MaximumCellCharacters)
                    throw new InvalidDataException($"A cell exceeds {EditableTableDocument.MaximumCellCharacters:N0} characters.");
                endedWithRowBreak = false;
            }
        }

        if (inQuotes) throw new InvalidDataException("Delimited table contains an unclosed quoted cell.");
        if (!endedWithRowBreak || row.Count > 0 || cell.Length > 0) CommitRow();
        if (rows.Count == 0) return EditableTableDocument.Create([], 0);
        return EditableTableDocument.Create(rows, maxColumnsSeen);
    }
}

public sealed record TableCellDifference(int Row, int Column, string Left, string Right);

public sealed record TableDiffResult(
    int LeftRows,
    int LeftColumns,
    int RightRows,
    int RightColumns,
    IReadOnlyList<TableCellDifference> Changes,
    bool IsTruncated);

public static class TableDiffEngine
{
    public const int MaximumChanges = 1_000;

    public static TableDiffResult Compare(EditableTableDocument left, EditableTableDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var changes = new List<TableCellDifference>(Math.Min(MaximumChanges, 128));
        var maxRows = Math.Max(left.RowCount, right.RowCount);
        var maxColumns = Math.Max(left.ColumnCount, right.ColumnCount);
        var truncated = false;

        for (var row = 0; row < maxRows && !truncated; row++)
        {
            for (var column = 0; column < maxColumns; column++)
            {
                var a = row < left.RowCount && column < left.ColumnCount ? left.DisplayCellAt(row, column) : string.Empty;
                var b = row < right.RowCount && column < right.ColumnCount ? right.DisplayCellAt(row, column) : string.Empty;
                if (string.Equals(a, b, StringComparison.Ordinal)) continue;
                if (changes.Count == MaximumChanges)
                {
                    truncated = true;
                    break;
                }
                changes.Add(new TableCellDifference(row, column, a, b));
            }
        }

        return new TableDiffResult(left.RowCount, left.ColumnCount, right.RowCount, right.ColumnCount, changes, truncated);
    }
}
