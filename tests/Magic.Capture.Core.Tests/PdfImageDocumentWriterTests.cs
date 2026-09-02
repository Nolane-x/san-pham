using System.Text;
using Magic.Capture.Core.Export;

namespace Magic.Capture.Core.Tests;

public sealed class PdfImageDocumentWriterTests
{
    [Fact]
    public void WritesDeterministicMultiPagePdfStructure()
    {
        var pages = new[]
        {
            new PdfJpegPage([0xFF, 0xD8, 0x01, 0xFF, 0xD9], 800, 600),
            new PdfJpegPage([0xFF, 0xD8, 0x02, 0xFF, 0xD9], 600, 900)
        };

        var first = PdfImageDocumentWriter.Write(pages);
        var second = PdfImageDocumentWriter.Write(pages);
        var text = Encoding.Latin1.GetString(first);

        Assert.Equal(first, second);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Count 2", text);
        Assert.Equal(2, Count(text, "/Subtype /Image"));
        Assert.Contains("xref", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void RejectsEmptyOrUnsafePageInputs()
    {
        Assert.Throws<ArgumentException>(() => PdfImageDocumentWriter.Write([]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfImageDocumentWriter.Write([new PdfJpegPage([1, 2, 3], 0, 10)]));
    }

    [Fact]
    public void InMemoryJpegPayloadBudgetIsBounded()
    {
        Assert.Equal(96L * 1024 * 1024, PdfImageDocumentWriter.MaximumJpegPayloadBytes);
    }


    [Fact]
    public void SessionEmitsPagesSequentiallyAndCompletesValidStructure()
    {
        using var output = new MemoryStream();
        var session = new PdfImageDocumentSession(output);
        session.AddPage(new PdfJpegPage([0xFF, 0xD8, 0x01, 0xFF, 0xD9], 320, 200));
        session.AddPage(new PdfJpegPage([0xFF, 0xD8, 0x02, 0xFF, 0xD9], 200, 320));
        session.Complete();

        var text = Encoding.Latin1.GetString(output.ToArray());
        Assert.Equal(2, session.PageCount);
        Assert.Contains("/Count 2", text);
        Assert.Equal(2, Count(text, "/Subtype /Image"));
        Assert.EndsWith("%%EOF\n", text);
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
