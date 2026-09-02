using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class TableExtractorBoundsTests
{
    [Fact]
    public void Extractor_caps_source_words_before_materializing_working_set()
    {
        var words = Enumerable.Range(0, TableExtractor.MaximumInputWords + 500)
            .Select(i => new OcrWord($"W{i}", new PixelRect((i % 8) * 100, (i / 8) * 24, 40, 18)))
            .ToArray();
        var document = new OcrDocument("large", [new OcrLine("large", new PixelRect(0, 0, 800, 30_000), words)], null);

        var table = TableExtractor.TryExtract(document);

        Assert.True(table is null || table.RowCount <= TableExtractor.MaximumOutputRows);
        Assert.True(table is null || table.ColumnCount <= TableExtractor.MaximumOutputColumns);
    }

    [Fact]
    public void Extractor_bounds_merged_cell_text()
    {
        var longWord = new string('x', TableExtractor.MaximumCellCharacters);
        var rows = new List<OcrLine>();
        for (var y = 0; y < 2; y++)
        {
            rows.Add(new OcrLine("row", new PixelRect(0, y * 40, 600, 20), [
                new OcrWord(longWord, new PixelRect(0, y * 40, 150, 20)),
                new OcrWord(longWord, new PixelRect(160, y * 40, 150, 20)),
                new OcrWord("B", new PixelRect(500, y * 40, 40, 20))
            ]));
        }

        var table = TableExtractor.TryExtract(new OcrDocument("rows", rows, null), new TableExtractionOptions(CellGapFactor: 2.0));

        Assert.NotNull(table);
        Assert.All(table!.Rows.SelectMany(row => row), cell => Assert.True(cell.Length <= TableExtractor.MaximumCellCharacters));
    }
}
