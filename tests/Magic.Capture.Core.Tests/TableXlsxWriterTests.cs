using System.IO.Compression;
using System.Text;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class TableXlsxWriterTests
{
    [Fact]
    public void Xlsx_contains_minimal_workbook_parts_inline_strings_and_merges()
    {
        var doc = EditableTableDocument.Create(
            [["=not a formula", "hidden follower"], ["line\none", "A&B"]],
            2,
            [new TableMergeRegion(new TableCellRange(0, 0, 0, 1))]);

        var bytes = TableXlsxWriter.Write(doc);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);

        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        Assert.Contains("inlineStr", xml);
        Assert.Contains("=not a formula", xml);
        Assert.DoesNotContain("<f>", xml);
        Assert.Contains("mergeCell", xml);
        Assert.Contains("A1:B1", xml);
        Assert.Contains("A&amp;B", xml);
    }
}
