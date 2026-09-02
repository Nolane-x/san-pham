using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;

namespace Magic.Capture.Core.Tests;

public sealed class OcrSpatialIndexTests
{
    [Fact]
    public void HitTest_finds_word_and_line_without_materializing_ui_elements()
    {
        var document = new OcrDocument("Hello world", [
            new OcrLine("Hello world", new PixelRect(10, 10, 120, 20), [
                new OcrWord("Hello", new PixelRect(10, 10, 45, 20)),
                new OcrWord("world", new PixelRect(70, 10, 50, 20))
            ])
        ], null);
        var index = OcrSpatialIndex.Create(document);

        Assert.Equal("Hello", index.FindWord(new PixelPoint(20, 15))?.Text);
        Assert.Equal("Hello world", index.FindLine(new PixelPoint(90, 15))?.Text);
    }

    [Fact]
    public void Search_uses_words_for_single_token_and_lines_for_phrase_queries()
    {
        var document = new OcrDocument("Submit now", [
            new OcrLine("Submit now", new PixelRect(0, 0, 120, 20), [
                new OcrWord("Submit", new PixelRect(0, 0, 60, 20)),
                new OcrWord("now", new PixelRect(70, 0, 30, 20))
            ])
        ], null);
        var index = OcrSpatialIndex.Create(document);

        Assert.Equal(OcrSpatialMatchKind.Word, Assert.Single(index.Search("submit")).Kind);
        Assert.Equal(OcrSpatialMatchKind.Line, Assert.Single(index.Search("submit now")).Kind);
    }

    [Fact]
    public void Search_is_bounded()
    {
        var words = Enumerable.Range(0, 600)
            .Select(i => new OcrWord("match", new PixelRect((i % 50) * 20, (i / 50) * 20, 18, 18)))
            .ToArray();
        var document = new OcrDocument("many", [new OcrLine("many", new PixelRect(0, 0, 1000, 300), words)], null);
        var index = OcrSpatialIndex.Create(document);

        Assert.Equal(OcrSpatialIndex.MaximumSearchMatches, index.Search("match").Count);
    }

    [Fact]
    public void DetailedSearch_marks_truncation_only_when_an_additional_match_exists()
    {
        var exactly = Enumerable.Range(0, OcrSpatialIndex.MaximumSearchMatches)
            .Select(i => new OcrWord("match", new PixelRect((i % 32) * 24, (i / 32) * 24, 20, 20)))
            .ToArray();
        var exactIndex = OcrSpatialIndex.Create(new OcrDocument("exact", [
            new OcrLine("exact", new PixelRect(0, 0, 800, 400), exactly)
        ], null));
        var extraIndex = OcrSpatialIndex.Create(new OcrDocument("extra", [
            new OcrLine("extra", new PixelRect(0, 0, 800, 400), exactly.Append(new OcrWord("match", new PixelRect(0, 300, 20, 20))).ToArray())
        ], null));

        Assert.False(exactIndex.SearchDetailed("match").IsTruncated);
        Assert.True(extraIndex.SearchDetailed("match").IsTruncated);
        Assert.Equal(OcrSpatialIndex.MaximumSearchMatches, extraIndex.SearchDetailed("match").Matches.Count);
    }

    [Fact]
    public void BlockHitTest_groups_paragraph_lines_but_keeps_columns_separate()
    {
        var document = new OcrDocument("Left one\nRight one\nLeft two\nRight two", [
            Line("Left one", 10, 10, 120),
            Line("Right one", 300, 10, 120),
            Line("Left two", 10, 36, 120),
            Line("Right two", 300, 36, 120)
        ], null);
        var index = OcrSpatialIndex.Create(document);

        var left = index.FindBlock(new PixelPoint(20, 40));
        var right = index.FindBlock(new PixelPoint(320, 40));

        Assert.Equal("Left one\r\nLeft two", left?.Text);
        Assert.Equal("Right one\r\nRight two", right?.Text);
        Assert.Equal(2, index.BlockCount);
    }

    private static OcrLine Line(string text, int x, int y, int width) =>
        new(text, new PixelRect(x, y, width, 18), [new OcrWord(text, new PixelRect(x, y, width, 18))]);
}
