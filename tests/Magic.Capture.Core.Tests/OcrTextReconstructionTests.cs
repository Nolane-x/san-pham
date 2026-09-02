using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;

namespace Magic.Capture.Core.Tests;

public sealed class OcrTextReconstructionTests
{
    [Fact]
    public void Layout_inserts_paragraph_break_for_large_vertical_gap()
    {
        var document = new OcrDocument("A\nB", [
            new OcrLine("First paragraph", new PixelRect(10, 10, 130, 20), [new OcrWord("First", new PixelRect(10, 10, 45, 20))]),
            new OcrLine("Second paragraph", new PixelRect(10, 80, 150, 20), [new OcrWord("Second", new PixelRect(10, 80, 55, 20))])
        ], null);

        var text = OcrTextReconstruction.Build(document, OcrTextReconstructionMode.Layout);

        Assert.Contains("First paragraph\r\n\r\nSecond paragraph", text);
    }

    [Fact]
    public void Code_reconstructs_geometry_based_indentation()
    {
        var document = new OcrDocument("if true\nreturn 1", [
            new OcrLine("if true", new PixelRect(0, 0, 70, 20), [
                new OcrWord("if", new PixelRect(0, 0, 20, 20)), new OcrWord("true", new PixelRect(30, 0, 40, 20))]),
            new OcrLine("return 1", new PixelRect(40, 30, 80, 20), [
                new OcrWord("return", new PixelRect(40, 30, 60, 20)), new OcrWord("1", new PixelRect(110, 30, 10, 20))])
        ], null);

        var text = OcrTextReconstruction.Build(document, OcrTextReconstructionMode.Code);
        var lines = text.Split("\r\n");

        Assert.StartsWith("if", lines[0]);
        Assert.StartsWith("    ", lines[1]);
        Assert.Contains("return", lines[1]);
    }
}
