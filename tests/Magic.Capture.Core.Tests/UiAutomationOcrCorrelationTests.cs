using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Tests;

public sealed class UiAutomationOcrCorrelationTests
{
    [Fact]
    public void Correlate_matches_rendered_words_to_the_smallest_overlapping_control()
    {
        var controls = new[]
        {
            Node("window", "Window", new PixelRect(0, 0, 500, 300)),
            Node("submit", "Button", new PixelRect(90, 90, 120, 40), "Submit")
        };
        var ocr = new OcrDocument("Submit", [
            new OcrLine("Submit", new PixelRect(100, 100, 70, 18), [
                new OcrWord("Submit", new PixelRect(100, 100, 70, 18))
            ])
        ], null);

        var result = UiAutomationOcrCorrelation.Correlate(controls, ocr);

        var submit = Assert.Single(result.Where(pair => pair.Key == "submit")).Value;
        Assert.Equal("Submit", submit.Text);
        Assert.Equal(["w1"], submit.WordIds);
        Assert.False(result.ContainsKey("window"));
    }

    [Fact]
    public void Correlate_is_bounded_per_control()
    {
        var words = Enumerable.Range(0, 80)
            .Select(i => new OcrWord($"W{i}", new PixelRect(10 + i * 6, 20, 10, 10)))
            .ToArray();
        var ocr = new OcrDocument(string.Join(" ", words.Select(word => word.Text)), [
            new OcrLine("many", new PixelRect(0, 0, 700, 60), words)
        ], null);
        var controls = new[] { Node("edit", "Edit", new PixelRect(0, 0, 700, 60)) };

        var result = UiAutomationOcrCorrelation.Correlate(controls, ocr);

        var correlation = result["edit"];
        Assert.InRange(correlation.WordIds.Count, 1, UiAutomationOcrCorrelation.MaximumEvidenceWordsPerNode);
        Assert.InRange(correlation.Text.Length, 1, UiAutomationOcrCorrelation.MaximumEvidenceTextLength);
    }

    [Fact]
    public void Correlate_ignores_password_controls()
    {
        var controls = new[]
        {
            new ScreenUiAutomationNode("password", "Edit", "Password", "password", null, true, null, null, true,
                new PixelRect(0, 0, 180, 30), null, null, null, null, 1, null, true)
        };
        var ocr = new OcrDocument("hunter2", [
            new OcrLine("hunter2", new PixelRect(5, 5, 70, 16), [new OcrWord("hunter2", new PixelRect(5, 5, 70, 16))])
        ], null);

        var result = UiAutomationOcrCorrelation.Correlate(controls, ocr);

        Assert.False(result.ContainsKey("password"));
    }

    private static ScreenUiAutomationNode Node(string key, string type, PixelRect bounds, string? name = null) =>
        new(key, type, name, null, null, true, null, null, false, bounds, null, null, "app", "Window", 123);
}
