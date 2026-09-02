using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.ScreenGraph;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class ScreenGraphTests
{
    [Fact]
    public void Builder_assigns_stable_ids_and_preserves_ocr_bounds()
    {
        var ocr = new OcrDocument("Hello world", [
            new OcrLine("Hello world", new PixelRect(10, 20, 120, 20), [
                new OcrWord("Hello", new PixelRect(10, 20, 45, 20)),
                new OcrWord("world", new PixelRect(65, 20, 50, 20))
            ])
        ], null);

        var graph = ScreenGraphBuilder.Build(new ScreenGraphBuildInput(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.UnixEpoch,
            "Region", "Editor", 400, 200, new PixelRect(0, 0, 400, 200), ocr, null, []));

        Assert.Equal("doc", graph.Nodes[0].Id);
        Assert.Equal("l1", graph.Nodes.First(n => n.Kind == ScreenNodeKind.TextLine).Id);
        Assert.Equal("w1", graph.Nodes.First(n => n.Kind == ScreenNodeKind.Word).Id);
        Assert.Equal(new PixelRect(10, 20, 45, 20), graph.Find("w1")!.Bounds);
    }

    [Fact]
    public void Builder_adds_table_and_barcode_nodes()
    {
        var table = new DetectedTable([["A", "B"], ["1", "2"]], 2, 2, .9, new PixelRect(5, 5, 100, 40));
        var graph = ScreenGraphBuilder.Build(new ScreenGraphBuildInput(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Region", null, 200, 100,
            new PixelRect(0, 0, 200, 100), new OcrDocument("", [], null), table,
            [new ScreenBarcode("QR_CODE", "https://example.com", new PixelRect(120, 10, 60, 60))]));

        Assert.Contains(graph.Nodes, n => n.Id == "t1" && n.Kind == ScreenNodeKind.Table);
        Assert.Contains(graph.Nodes, n => n.Id == "b1" && n.Text == "https://example.com");
    }
}
