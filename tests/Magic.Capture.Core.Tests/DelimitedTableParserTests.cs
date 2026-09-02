using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class DelimitedTableParserTests
{
    [Fact]
    public void Csv_parser_handles_quotes_commas_newlines_and_empty_cells()
    {
        var doc = DelimitedTableParser.Parse("Name,Note,Empty\r\nA,\"hello, world\",\r\nB,\"two\nlines\",x", ',');

        Assert.Equal(3, doc.RowCount);
        Assert.Equal(3, doc.ColumnCount);
        Assert.Equal("hello, world", doc.CellAt(1, 1));
        Assert.Equal(string.Empty, doc.CellAt(1, 2));
        Assert.Equal("two\nlines", doc.CellAt(2, 1));
    }

    [Fact]
    public void Parser_rejects_unclosed_quote()
    {
        Assert.Throws<InvalidDataException>(() => DelimitedTableParser.Parse("A,\"broken", ','));
    }

    [Fact]
    public void Diff_is_bounded_and_reports_dimension_changes()
    {
        var leftRows = Enumerable.Range(0, 40).Select(r => (IReadOnlyList<string>)Enumerable.Range(0, 40).Select(c => $"L{r}-{c}").ToArray()).ToArray();
        var rightRows = Enumerable.Range(0, 41).Select(r => (IReadOnlyList<string>)Enumerable.Range(0, 40).Select(c => $"R{r}-{c}").ToArray()).ToArray();
        var left = EditableTableDocument.Create(leftRows, 40);
        var right = EditableTableDocument.Create(rightRows, 40);

        var diff = TableDiffEngine.Compare(left, right);

        Assert.True(diff.IsTruncated);
        Assert.Equal(TableDiffEngine.MaximumChanges, diff.Changes.Count);
        Assert.Equal(40, diff.LeftRows);
        Assert.Equal(41, diff.RightRows);
    }
    [Fact]
    public void Csv_parser_unescapes_doubled_quotes()
    {
        var doc = DelimitedTableParser.Parse("A,\"say \"\"hello\"\"\"", ',');

        Assert.Equal("say \"hello\"", doc.CellAt(0, 1));
    }

}
