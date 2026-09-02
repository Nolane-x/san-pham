using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Tests;

public sealed class ScreenGraphUiAutomationTests
{
    [Fact]
    public void Builder_merges_uia_nodes_without_changing_existing_id_spaces()
    {
        var input = new ScreenGraphBuildInput(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Region", "App", 800, 600,
            new PixelRect(0, 0, 800, 600), new OcrDocument("Submit", [new OcrLine("Submit", new PixelRect(10, 10, 80, 20), [])], null), null, [],
            [
                new ScreenUiAutomationNode("root", "Window", "App", null, null, true, null, null, false, new PixelRect(0, 0, 800, 600), null, null, "app.exe", "App"),
                new ScreenUiAutomationNode("submit", "Button", "Submit", "submitButton", null, true, false, false, true, new PixelRect(10, 10, 80, 30), "root", "Alt+S", "app.exe", "App")
            ]);
        var graph = ScreenGraphBuilder.Build(input);
        Assert.Contains(graph.Nodes, n => n.Id == "l1" && n.Kind == ScreenNodeKind.TextLine);
        var button = graph.Nodes.Single(n => n.Id == "u2");
        Assert.Equal(ScreenNodeKind.UiAutomation, button.Kind);
        Assert.Equal("u1", button.ParentId);
        Assert.Equal("Button", button.Attributes!["controlType"]);
        Assert.Equal("submitButton", button.Attributes["automationId"]);
    }

    [Fact]
    public void Builder_ignores_duplicate_uia_stable_keys_instead_of_emitting_duplicate_node_ids()
    {
        var input = new ScreenGraphBuildInput(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Region", null, 100, 100,
            new PixelRect(0, 0, 100, 100), new OcrDocument(string.Empty, [], null), null, [],
            [
                new ScreenUiAutomationNode("same", "Button", "A", null, null, true, null, null, false, new PixelRect(1, 1, 10, 10), null, null, null, null),
                new ScreenUiAutomationNode("same", "Button", "B", null, null, true, null, null, false, new PixelRect(20, 1, 10, 10), null, null, null, null)
            ]);

        var graph = ScreenGraphBuilder.Build(input);
        var uia = graph.Nodes.Where(n => n.Kind == ScreenNodeKind.UiAutomation).ToArray();
        Assert.Single(uia);
        Assert.Equal("A", uia[0].Text);
    }
    [Fact]
    public void Builder_correlates_uia_controls_with_overlapping_ocr_word_ids()
    {
        var input = new ScreenGraphBuildInput(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Region", null, 300, 150,
            new PixelRect(0, 0, 300, 150),
            new OcrDocument("Submit", [new OcrLine("Submit", new PixelRect(110, 55, 70, 18),
                [new OcrWord("Submit", new PixelRect(110, 55, 70, 18))])], null),
            null, [],
            [new ScreenUiAutomationNode("submit", "Button", "Submit", "submitButton", null, true, null, null, true,
                new PixelRect(100, 45, 100, 40), null, "Alt+S", "app.exe", "App")]);

        var graph = ScreenGraphBuilder.Build(input);

        var button = graph.Nodes.Single(node => node.Kind == ScreenNodeKind.UiAutomation);
        Assert.Equal("Submit", button.Attributes!["ocrText"]);
        Assert.Equal("w1", button.Attributes["ocrWordIds"]);
        Assert.Equal("1", button.Attributes["ocrWordCount"]);
    }

}
