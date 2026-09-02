using System.Text;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.Tables;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Magic.Capture.App.Views;

public sealed partial class TableWorkspaceWindow : Window
{
    private const int PageRows = 64;
    private const int PageColumns = 16;
    private const ulong MaximumCompareFileBytes = 2UL * 1024 * 1024;
    private const int MaximumDisplayedDiffs = 200;
    private const int MaximumUndoStates = 20;

    private sealed record TableCellView(int Row, int Column, string DisplayText, string ToolTip);
    private sealed record TableRowView(string RowLabel, IReadOnlyList<TableCellView> Cells);

    private readonly ApplicationServices _services;
    private readonly List<EditableTableDocument> _undoHistory = [];
    private readonly List<EditableTableDocument> _redoHistory = [];
    private readonly Dictionary<long, Button> _visibleCellButtons = [];
    private EditableTableDocument _document;
    private int _rowStart;
    private int _columnStart;
    private int _anchorRow;
    private int _anchorColumn;
    private int _focusRow;
    private int _focusColumn;

    internal TableWorkspaceWindow(DetectedTable table, ApplicationServices services)
    {
        InitializeComponent();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _document = EditableTableDocument.FromDetectedTable(table ?? throw new ArgumentNullException(nameof(table)));
        _anchorRow = _focusRow = _document.RowCount > 0 ? 0 : -1;
        _anchorColumn = _focusColumn = _document.ColumnCount > 0 ? 0 : -1;
        Title = "Magic Capture Desktop — Table Workspace";
        Platform.WindowHelpers.MoveAndResize(this, 120, 80, 1240, 820);
        RebuildPage();
        StatusText.Text += " · edits stay in this workspace until copied or exported";
    }

    private TableCellRange? CurrentRange => _anchorRow >= 0 && _anchorColumn >= 0 && _focusRow >= 0 && _focusColumn >= 0
        ? TableCellRange.Normalize(_anchorRow, _anchorColumn, _focusRow, _focusColumn)
        : null;

    private void RebuildPage()
    {
        if (_document.RowCount == 0 || _document.ColumnCount == 0)
        {
            _rowStart = _columnStart = 0;
            TableRowsRepeater.ItemsSource = Array.Empty<TableRowView>();
            ColumnHeaderPanel.Children.Clear();
            CellEditorTextBox.Text = string.Empty;
            CellEditorTextBox.IsEnabled = false;
            SelectionText.Text = "Empty table";
            StatusText.Text = $"0×{_document.ColumnCount} · no cells to display";
            return;
        }

        _rowStart = Math.Clamp(_rowStart, 0, Math.Max(0, _document.RowCount - 1));
        _columnStart = Math.Clamp(_columnStart, 0, Math.Max(0, _document.ColumnCount - 1));
        _focusRow = Math.Clamp(_focusRow, 0, _document.RowCount - 1);
        _focusColumn = Math.Clamp(_focusColumn, 0, _document.ColumnCount - 1);
        _anchorRow = Math.Clamp(_anchorRow, 0, _document.RowCount - 1);
        _anchorColumn = Math.Clamp(_anchorColumn, 0, _document.ColumnCount - 1);

        var rowEnd = Math.Min(_document.RowCount, _rowStart + PageRows);
        var columnEnd = Math.Min(_document.ColumnCount, _columnStart + PageColumns);
        var selection = CurrentRange;
        _visibleCellButtons.Clear();

        ColumnHeaderPanel.Children.Clear();
        ColumnHeaderPanel.Children.Add(new TextBlock { Text = "", Width = 54, Height = 30 });
        for (var column = _columnStart; column < columnEnd; column++)
        {
            ColumnHeaderPanel.Children.Add(new TextBlock
            {
                Text = ColumnName(column), Width = 122, Height = 30,
                TextAlignment = TextAlignment.Center
            });
        }

        var rows = new List<TableRowView>(rowEnd - _rowStart);
        for (var row = _rowStart; row < rowEnd; row++)
        {
            var cells = new List<TableCellView>(columnEnd - _columnStart);
            for (var column = _columnStart; column < columnEnd; column++)
            {
                var merge = _document.MergeAt(row, column);
                var follower = merge is not null && (merge.AnchorRow != row || merge.AnchorColumn != column);
                var raw = _document.CellAt(row, column);
                var visible = follower ? "↳ merged" : BoundCellPreview(raw);
                if (merge is not null && !follower) visible = "⧉ " + visible;
                var tip = $"R{row + 1}C{column + 1}" + (merge is null ? string.Empty : $" · merged {RangeLabel(merge.Range)}") + $"\n{BoundCellPreview(raw, 240)}";
                cells.Add(new TableCellView(row, column, visible, tip));
            }
            rows.Add(new TableRowView((row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), cells));
        }
        TableRowsRepeater.ItemsSource = rows;
        CellEditorTextBox.IsEnabled = true;
        CellEditorTextBox.Text = _document.CellAt(_focusRow, _focusColumn);
        SelectionText.Text = selection is { } range ? $"{RangeLabel(range)} · active R{_focusRow + 1}C{_focusColumn + 1}" : string.Empty;
        StatusText.Text = $"{_document.RowCount}×{_document.ColumnCount} · {_document.Merges.Count} merge{(_document.Merges.Count == 1 ? string.Empty : "s")} · showing rows {_rowStart + 1}–{rowEnd}, columns {ColumnName(_columnStart)}–{ColumnName(columnEnd - 1)} · max {PageRows}×{PageColumns} controls/page";
    }

    private void TableCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TableCellView cell } button) return;
        if (ExtendSelectionToggle.IsChecked == true && CurrentRange is not null)
        {
            _focusRow = cell.Row;
            _focusColumn = cell.Column;
            ExtendSelectionToggle.IsChecked = false;
        }
        else
        {
            _anchorRow = _focusRow = cell.Row;
            _anchorColumn = _focusColumn = cell.Column;
        }
        UpdateVisibleSelectionStyles();
        UpdateSelectionEditor();
    }

    private void TableCell_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TableCellView cell } button) return;
        _visibleCellButtons[CellKey(cell.Row, cell.Column)] = button;
        ApplySelectionStyle(button, cell.Row, cell.Column);
    }

    private void UpdateVisibleSelectionStyles()
    {
        foreach (var pair in _visibleCellButtons)
        {
            var row = (int)(pair.Key >> 32);
            var column = (int)(uint)pair.Key;
            ApplySelectionStyle(pair.Value, row, column);
        }
    }

    private void ApplySelectionStyle(Button button, int row, int column)
    {
        if (row == _focusRow && column == _focusColumn)
            button.BorderThickness = new Thickness(3);
        else if (CurrentRange is { } range && range.Contains(row, column))
            button.BorderThickness = new Thickness(2);
        else
            button.BorderThickness = new Thickness(1);
    }

    private static long CellKey(int row, int column) => ((long)row << 32) | (uint)column;

    private void UpdateSelectionEditor()
    {
        if (_focusRow < 0 || _focusColumn < 0 || _focusRow >= _document.RowCount || _focusColumn >= _document.ColumnCount) return;
        CellEditorTextBox.Text = _document.CellAt(_focusRow, _focusColumn);
        if (CurrentRange is { } range)
            SelectionText.Text = $"{RangeLabel(range)} · active R{_focusRow + 1}C{_focusColumn + 1}";
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoHistory.Count == 0) { StatusText.Text = "Nothing to undo."; return; }
        PushHistory(_redoHistory, _document);
        _document = _undoHistory[^1];
        _undoHistory.RemoveAt(_undoHistory.Count - 1);
        ClampSelectionAfterHistory();
        RebuildPage();
        StatusText.Text = "Undo applied · " + StatusText.Text;
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoHistory.Count == 0) { StatusText.Text = "Nothing to redo."; return; }
        PushHistory(_undoHistory, _document);
        _document = _redoHistory[^1];
        _redoHistory.RemoveAt(_redoHistory.Count - 1);
        ClampSelectionAfterHistory();
        RebuildPage();
        StatusText.Text = "Redo applied · " + StatusText.Text;
    }

    private void ApplyCell_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        EnsureActiveCell();
        _document = TableDocumentOperations.SetCell(_document, _focusRow, _focusColumn, CellEditorTextBox.Text);
        return "Cell updated.";
    });

    private void InsertRow_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        var index = _focusRow >= 0 ? _focusRow + 1 : _document.RowCount;
        _document = TableDocumentOperations.InsertRow(_document, index);
        _anchorRow = _focusRow = Math.Min(index, _document.RowCount - 1);
        if (_anchorColumn < 0 && _document.ColumnCount > 0) _anchorColumn = _focusColumn = 0;
        EnsureFocusVisible();
        return $"Inserted row {index + 1}.";
    });

    private void DeleteRow_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        EnsureActiveCell();
        if (_document.RowCount <= 1) throw new InvalidOperationException("Keep at least one row in the workspace.");
        var deleted = _focusRow;
        _document = TableDocumentOperations.DeleteRow(_document, deleted);
        _anchorRow = _focusRow = Math.Min(deleted, _document.RowCount - 1);
        EnsureFocusVisible();
        return $"Deleted row {deleted + 1}.";
    });

    private void InsertColumn_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        var index = _focusColumn >= 0 ? _focusColumn + 1 : _document.ColumnCount;
        _document = TableDocumentOperations.InsertColumn(_document, index);
        _anchorColumn = _focusColumn = Math.Min(index, _document.ColumnCount - 1);
        if (_anchorRow < 0 && _document.RowCount > 0) _anchorRow = _focusRow = 0;
        EnsureFocusVisible();
        return $"Inserted column {ColumnName(index)}.";
    });

    private void DeleteColumn_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        EnsureActiveCell();
        if (_document.ColumnCount <= 1) throw new InvalidOperationException("Keep at least one column in the workspace.");
        var deleted = _focusColumn;
        _document = TableDocumentOperations.DeleteColumn(_document, deleted);
        _anchorColumn = _focusColumn = Math.Min(deleted, _document.ColumnCount - 1);
        EnsureFocusVisible();
        return $"Deleted column {ColumnName(deleted)}.";
    });

    private void Merge_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        var range = CurrentRange ?? throw new InvalidOperationException("Select a cell range first.");
        _document = TableDocumentOperations.Merge(_document, range);
        return $"Merged {RangeLabel(range)}. Underlying values remain recoverable by Unmerge.";
    });

    private void Unmerge_Click(object sender, RoutedEventArgs e) => RunEdit(() =>
    {
        var range = CurrentRange ?? throw new InvalidOperationException("Select a merged cell or range first.");
        var before = _document.Merges.Count;
        _document = TableDocumentOperations.Unmerge(_document, range);
        return before == _document.Merges.Count ? "No merge intersects the selection." : "Merge removed; original cell values restored.";
    });

    private void CopySelection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var range = CurrentRange ?? throw new InvalidOperationException("Select cells first.");
            _services.Clipboard.CopyText(TableDocumentOperations.CopySelectionTsv(_document, range));
            StatusText.Text = $"Copied {RangeLabel(range)} as TSV.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bytes = TableXlsxWriter.Write(_document);
            var file = await _services.Export.SaveBytesAsAsync(this, bytes, "Excel workbook", ".xlsx", "Magic Capture Desktop table");
            StatusText.Text = file is null ? "XLSX export cancelled." : $"Saved {file.Name}";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void CompareFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".tsv");
            picker.FileTypeFilter.Add(".txt");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > MaximumCompareFileBytes)
                throw new InvalidDataException($"Compare file exceeds the supported {MaximumCompareFileBytes / (1024 * 1024)} MB limit.");
            var text = await FileIO.ReadTextAsync(file);
            var delimiter = file.FileType.Equals(".tsv", StringComparison.OrdinalIgnoreCase) || (!file.FileType.Equals(".csv", StringComparison.OrdinalIgnoreCase) && text.Contains('\t')) ? '\t' : ',';
            var other = DelimitedTableParser.Parse(text, delimiter);
            var diff = TableDiffEngine.Compare(_document, other);
            DiffOutputTextBox.Text = BuildDiffText(file.Name, diff);
            StatusText.Text = $"Compared with {file.Name}: {diff.Changes.Count}{(diff.IsTruncated ? "+" : string.Empty)} changed cells.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            StatusText.Text = ex.Message;
        }
    }

    private void PreviousRows_Click(object sender, RoutedEventArgs e) { _rowStart = Math.Max(0, _rowStart - PageRows); RebuildPage(); }
    private void NextRows_Click(object sender, RoutedEventArgs e) { if (_rowStart + PageRows < _document.RowCount) _rowStart += PageRows; RebuildPage(); }
    private void PreviousColumns_Click(object sender, RoutedEventArgs e) { _columnStart = Math.Max(0, _columnStart - PageColumns); RebuildPage(); }
    private void NextColumns_Click(object sender, RoutedEventArgs e) { if (_columnStart + PageColumns < _document.ColumnCount) _columnStart += PageColumns; RebuildPage(); }

    private void RunEdit(Func<string> action)
    {
        try
        {
            var before = _document;
            var message = action();
            if (!ReferenceEquals(before, _document))
            {
                PushHistory(_undoHistory, before);
                _redoHistory.Clear();
            }
            DiffOutputTextBox.Text = string.Empty;
            RebuildPage();
            StatusText.Text = message + " · " + StatusText.Text;
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            StatusText.Text = ex.Message;
        }
    }

    private static void PushHistory(List<EditableTableDocument> history, EditableTableDocument document)
    {
        if (history.Count == MaximumUndoStates) history.RemoveAt(0);
        history.Add(document);
    }

    private void ClampSelectionAfterHistory()
    {
        if (_document.RowCount == 0 || _document.ColumnCount == 0)
        {
            _anchorRow = _focusRow = _anchorColumn = _focusColumn = -1;
            return;
        }
        _anchorRow = Math.Clamp(_anchorRow, 0, _document.RowCount - 1);
        _focusRow = Math.Clamp(_focusRow, 0, _document.RowCount - 1);
        _anchorColumn = Math.Clamp(_anchorColumn, 0, _document.ColumnCount - 1);
        _focusColumn = Math.Clamp(_focusColumn, 0, _document.ColumnCount - 1);
        EnsureFocusVisible();
    }

    private void EnsureActiveCell()
    {
        if (_focusRow < 0 || _focusColumn < 0 || _focusRow >= _document.RowCount || _focusColumn >= _document.ColumnCount)
            throw new InvalidOperationException("Select a cell first.");
    }

    private void EnsureFocusVisible()
    {
        if (_focusRow >= 0) _rowStart = (_focusRow / PageRows) * PageRows;
        if (_focusColumn >= 0) _columnStart = (_focusColumn / PageColumns) * PageColumns;
    }

    private static string BuildDiffText(string fileName, TableDiffResult diff)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Current: {diff.LeftRows}×{diff.LeftColumns}");
        builder.AppendLine($"{fileName}: {diff.RightRows}×{diff.RightColumns}");
        builder.AppendLine($"Changed cells: {diff.Changes.Count}{(diff.IsTruncated ? "+ (bounded)" : string.Empty)}");
        builder.AppendLine();
        foreach (var change in diff.Changes.Take(MaximumDisplayedDiffs))
            builder.Append("R").Append(change.Row + 1).Append('C').Append(change.Column + 1)
                .Append(": '").Append(BoundCellPreview(change.Left, 100)).Append("' → '").Append(BoundCellPreview(change.Right, 100)).AppendLine("'");
        if (diff.Changes.Count > MaximumDisplayedDiffs)
            builder.AppendLine($"+{diff.Changes.Count - MaximumDisplayedDiffs} additional changed cells not rendered in this view.");
        return builder.ToString();
    }

    private static string RangeLabel(TableCellRange range) =>
        range.StartRow == range.EndRow && range.StartColumn == range.EndColumn
            ? $"R{range.StartRow + 1}C{range.StartColumn + 1}"
            : $"R{range.StartRow + 1}C{range.StartColumn + 1}:R{range.EndRow + 1}C{range.EndColumn + 1}";

    private static string ColumnName(int column)
    {
        var value = column + 1;
        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        while (value > 0)
        {
            value--;
            buffer[--position] = (char)('A' + value % 26);
            value /= 26;
        }
        return new string(buffer[position..]);
    }

    private static string BoundCellPreview(string? value, int max = 70)
    {
        value ??= string.Empty;
        var normalized = value.Replace("\r", " ").Replace("\n", " ↵ ").Replace("\t", " ⇥ ");
        return normalized.Length <= max ? normalized : normalized[..Math.Max(0, max - 1)] + "…";
    }
}
