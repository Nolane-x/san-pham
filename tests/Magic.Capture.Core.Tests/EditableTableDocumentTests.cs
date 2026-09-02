using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class EditableTableDocumentTests
{
    [Fact]
    public void Edit_insert_delete_and_merge_preserve_rectangular_document()
    {
        var doc = EditableTableDocument.FromDetectedTable(new DetectedTable(
            [["A", "B"], ["C", "D"]], 2, 2, .9, PixelRect.Empty));

        doc = TableDocumentOperations.SetCell(doc, 1, 1, "Edited");
        doc = TableDocumentOperations.InsertRow(doc, 1);
        doc = TableDocumentOperations.InsertColumn(doc, 1);
        doc = TableDocumentOperations.Merge(doc, new TableCellRange(0, 0, 1, 1));

        Assert.Equal(3, doc.RowCount);
        Assert.Equal(3, doc.ColumnCount);
        Assert.Equal("Edited", doc.CellAt(2, 2));
        Assert.Single(doc.Merges);
        Assert.Equal(string.Empty, doc.DisplayCellAt(1, 1));

        doc = TableDocumentOperations.Unmerge(doc, new TableCellRange(1, 1, 1, 1));
        Assert.Empty(doc.Merges);
        Assert.Equal(string.Empty, doc.CellAt(1, 1));
    }

    [Fact]
    public void Structural_edits_transform_merge_ranges_deterministically()
    {
        var doc = EditableTableDocument.Create([
            ["A", "B", "C"], ["D", "E", "F"], ["G", "H", "I"]
        ], 3, [new TableMergeRegion(new TableCellRange(0, 0, 1, 1))]);

        doc = TableDocumentOperations.InsertRow(doc, 1);
        Assert.Equal(new TableCellRange(0, 0, 2, 1), Assert.Single(doc.Merges).Range);

        doc = TableDocumentOperations.DeleteColumn(doc, 0);
        Assert.Equal(new TableCellRange(0, 0, 2, 0), Assert.Single(doc.Merges).Range);
    }

    [Fact]
    public void CopySelection_preserves_empty_cells_and_hides_merge_followers()
    {
        var doc = EditableTableDocument.Create(
            [["A", "B"], ["", "D"]],
            2,
            [new TableMergeRegion(new TableCellRange(0, 0, 0, 1))]);

        var tsv = TableDocumentOperations.CopySelectionTsv(doc, new TableCellRange(0, 0, 1, 1));

        Assert.Equal("A\t\r\n\tD", tsv);
    }

    [Fact]
    public void Overlapping_merges_are_rejected()
    {
        var doc = EditableTableDocument.Create([["A", "B"], ["C", "D"]], 2);
        doc = TableDocumentOperations.Merge(doc, new TableCellRange(0, 0, 0, 1));

        Assert.Throws<InvalidOperationException>(() =>
            TableDocumentOperations.Merge(doc, new TableCellRange(0, 1, 1, 1)));
    }
    [Fact]
    public void CopySelection_quotes_tabs_newlines_and_quotes_without_losing_empty_cells()
    {
        var doc = EditableTableDocument.Create(
            [["A\tB", "line\n\"quoted\"", ""]],
            3);

        var tsv = TableDocumentOperations.CopySelectionTsv(doc, new TableCellRange(0, 0, 0, 2));

        Assert.Equal("\"A\tB\"\t\"line\n\"\"quoted\"\"\"\t", tsv);
    }

    [Fact]
    public void CopySelection_rejects_output_that_would_exceed_the_encoded_character_budget()
    {
        var quoteHeavy = new string('\"', EditableTableDocument.MaximumCellCharacters);
        var rows = Enumerable.Range(0, 488)
            .Select(_ => (IReadOnlyList<string>)[quoteHeavy])
            .ToArray();
        var doc = EditableTableDocument.Create(rows, 1);

        Assert.Throws<InvalidOperationException>(() =>
            TableDocumentOperations.CopySelectionTsv(doc, new TableCellRange(0, 0, 487, 0)));
    }

    [Fact]
    public void Cell_edit_is_copy_on_write_and_leaves_previous_document_unchanged()
    {
        var original = EditableTableDocument.Create([["A", "B"], ["C", "D"]], 2);

        var edited = TableDocumentOperations.SetCell(original, 1, 1, "Changed");

        Assert.Equal("D", original.CellAt(1, 1));
        Assert.Equal("Changed", edited.CellAt(1, 1));
        Assert.Equal("A", edited.CellAt(0, 0));
    }

}
