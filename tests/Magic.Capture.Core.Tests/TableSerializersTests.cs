using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class TableSerializersTests
{
    private static readonly DetectedTable Table = new(
        [["Name", "Note"], ["A, B", "He said \"hello\""], ["Pipe", "A|B"]],
        2, 3, 0.9, new PixelRect(0, 0, 100, 100));

    [Fact]
    public void CsvEscapesCommasAndQuotes()
    {
        var csv = TableSerializers.ToCsv(Table);
        Assert.Contains("\"A, B\"", csv);
        Assert.Contains("\"He said \"\"hello\"\"\"", csv);
    }

    [Fact]
    public void MarkdownEscapesPipes()
    {
        var md = TableSerializers.ToMarkdown(Table);
        Assert.Contains("A\\|B", md);
        Assert.Contains("| --- | --- |", md);
    }

    [Fact]
    public void HtmlEncodesCellText()
    {
        var table = new DetectedTable([["<tag>", "A&B"]], 2, 1, .8, PixelRect.Empty);
        var html = TableSerializers.ToHtml(table);
        Assert.Contains("&lt;tag&gt;", html);
        Assert.Contains("A&amp;B", html);
    }

    [Fact]
    public void JsonIsArrayOfArrays()
    {
        var json = TableSerializers.ToJson(Table);
        Assert.StartsWith("[[", json);
        Assert.Contains("\"Name\"", json);
    }

    [Fact]
    public void LocaleConversion_applies_to_first_data_row_too()
    {
        var table = new DetectedTable([["12.5"], ["13.5"]], 1, 2, .8, PixelRect.Empty);
        var vi = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

        var output = TableSerializers.ToDelimited(
            table,
            new TableDelimitedOptions('\t', TableNumberLocaleMode.CurrentCulture),
            vi);

        Assert.StartsWith("12,5", output);
        Assert.Contains("13,5", output);
    }

    [Fact]
    public void Serialization_rejects_output_beyond_character_budget()
    {
        var cell = new string('x', TableSerializers.MaximumOutputCharacters / 2 + 100);
        var table = new DetectedTable([[cell], [cell]], 1, 2, .8, PixelRect.Empty);

        Assert.Throws<InvalidOperationException>(() => TableSerializers.ToCsv(table));
    }

    [Fact]
    public void DelimitedExportsPreserveEmptyCells()
    {
        var table = new DetectedTable([["A", "B", "C"], ["1", "", "3"]], 3, 2, .8, PixelRect.Empty);

        Assert.Contains("1,,3", TableSerializers.ToCsv(table));
        Assert.Contains("1\t\t3", TableSerializers.ToTsv(table));
        Assert.Contains("\"\",\"3\"", TableSerializers.ToJson(table));
    }
}
