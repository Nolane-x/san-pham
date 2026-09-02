using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Signals;

namespace Magic.Capture.Core.Tests;

public sealed class TextSignalExtractorTests
{
    [Fact]
    public void Extracts_urls_emails_paths_and_stack_frames_without_ai()
    {
        var ocr = new OcrDocument("", [
            Line("Email dev@example.com https://example.com", 0),
            Line(@"at Demo.Run() in C:\src\Demo.cs:line 42", 30)
        ], null);

        var signals = TextSignalExtractor.Extract(ocr);

        Assert.Contains(signals, s => s.Kind == TextSignalKind.Email && s.Value == "dev@example.com");
        Assert.Contains(signals, s => s.Kind == TextSignalKind.Url && s.Value.StartsWith("https://example.com"));
        Assert.Contains(signals, s => s.Kind == TextSignalKind.FilePath && s.Value.Contains("Demo.cs"));
        Assert.Contains(signals, s => s.Kind == TextSignalKind.StackFrame && s.Value.Contains("Demo.Run"));
    }

    [Fact]
    public void Detects_exception_headline()
    {
        var ocr = new OcrDocument("", [Line("System.NullReferenceException: Object reference not set", 0)], null);
        var signals = TextSignalExtractor.Extract(ocr);
        Assert.Contains(signals, s => s.Kind == TextSignalKind.ErrorHeadline);
    }

    private static OcrLine Line(string text, int y) =>
        new(text, new PixelRect(0, y, 600, 24), [new OcrWord(text, new PixelRect(0, y, 600, 24))]);
}
