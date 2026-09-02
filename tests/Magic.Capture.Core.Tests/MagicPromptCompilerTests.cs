using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Tests;

public sealed class MagicPromptCompilerTests
{
    [Fact]
    public void Compiler_includes_evidence_contract_and_screen_node_ids()
    {
        var graph = new ScreenGraphDocument(1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, 100, 100, "Region", null,
            [new ScreenGraphNode("doc", ScreenNodeKind.Document, null, new PixelRect(0,0,100,100), 1, null, null),
             new ScreenGraphNode("w1", ScreenNodeKind.Word, "42", new PixelRect(10,10,20,10), .99, "doc", null)]);
        var prompt = MagicPromptCompiler.Compile(BuiltInMagicActions.ById("general.explain"), graph, null);
        Assert.Contains("evidence", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("w1", prompt);
        Assert.Contains("Do not invent", prompt, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Compiler_can_namespace_node_ids_for_multi_capture_context()
    {
        var graph = new ScreenGraphDocument(1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, 100, 100, "Region", null,
            [new ScreenGraphNode("w1", ScreenNodeKind.Word, "42", new PixelRect(10,10,20,10), .99, "doc", null)]);

        var text = MagicPromptCompiler.SerializeGraph(graph, nodePrefix: "c2");

        Assert.Contains("c2:w1", text);
        Assert.DoesNotContain("[w1]", text);
    }

}
