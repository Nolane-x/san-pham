using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class TableExtractorTests
{
    [Fact]
    public void ExtractsThreeByThreeTable()
    {
        var doc = Document(
            Row(10, ("Product", 10), ("Qty", 170), ("Price", 260)),
            Row(40, ("Mouse", 10), ("2", 170), ("25", 260)),
            Row(70, ("Keyboard", 10), ("1", 170), ("80", 260)));

        var table = TableExtractor.TryExtract(doc);

        Assert.NotNull(table);
        Assert.Equal(3, table!.ColumnCount);
        Assert.Equal(3, table.RowCount);
        Assert.Equal("Keyboard", table.Rows[2][0]);
        Assert.True(table.Confidence >= 0.52);
    }

    [Fact]
    public void MergesNearbyWordsIntoSingleCell()
    {
        var doc = Document(
            Line(10,
                Word("Product", 10, 10), Word("Name", 70, 10),
                Word("Qty", 220, 10), Word("Price", 310, 10)),
            Line(40,
                Word("Mechanical", 10, 40), Word("Keyboard", 90, 40),
                Word("1", 220, 40), Word("80", 310, 40)));

        var table = TableExtractor.TryExtract(doc);

        Assert.NotNull(table);
        Assert.Equal("Product Name", table!.Rows[0][0]);
        Assert.Equal("Mechanical Keyboard", table.Rows[1][0]);
    }

    [Fact]
    public void SmallVerticalOcrJitterStillFormsRows()
    {
        var doc = Document(
            Line(10, Word("A", 10, 10), Word("B", 150, 13)),
            Line(42, Word("1", 10, 42), Word("2", 150, 39)));

        var table = TableExtractor.TryExtract(doc);

        Assert.NotNull(table);
        Assert.Equal(2, table!.RowCount);
        Assert.Equal(2, table.ColumnCount);
    }

    [Fact]
    public void ParagraphDoesNotBecomeTable()
    {
        var doc = Document(
            Line(10, Word("This", 10, 10), Word("is", 48, 10), Word("a", 70, 10), Word("paragraph", 86, 10)),
            Line(40, Word("with", 10, 40), Word("normal", 48, 40), Word("word", 105, 40), Word("spacing", 145, 40)));

        Assert.Null(TableExtractor.TryExtract(doc));
    }

    [Fact]
    public void EmptyDocumentReturnsNull()
    {
        Assert.Null(TableExtractor.TryExtract(new OcrDocument("", [], null)));
    }

    private static OcrDocument Document(params OcrLine[] lines) =>
        new(string.Join("\n", lines.Select(x => x.Text)), lines, null);

    private static OcrLine Row(int y, params (string Text, int X)[] values) =>
        Line(y, values.Select(v => Word(v.Text, v.X, y)).ToArray());

    private static OcrLine Line(int y, params OcrWord[] words)
    {
        var left = words.Min(w => w.Bounds.X);
        var right = words.Max(w => w.Bounds.Right);
        return new OcrLine(string.Join(" ", words.Select(w => w.Text)), new PixelRect(left, y, right - left, 18), words);
    }

    private static OcrWord Word(string text, int x, int y) =>
        new(text, new PixelRect(x, y, Math.Max(12, text.Length * 8), 18));
}
