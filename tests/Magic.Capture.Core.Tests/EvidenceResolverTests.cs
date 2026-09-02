using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Tests;

public sealed class EvidenceResolverTests
{
    [Fact]
    public void Resolver_maps_known_ids_and_ignores_unknown_ids()
    {
        var graph = new ScreenGraphDocument(1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, 100,100,"Region",null,
            [new ScreenGraphNode("doc", ScreenNodeKind.Document, null, new PixelRect(0,0,100,100),1,null,null),
             new ScreenGraphNode("w1", ScreenNodeKind.Word, "hello", new PixelRect(5,6,30,10),1,"doc",null)]);
        var resolved = EvidenceResolver.Resolve(graph, ["w1", "does-not-exist"]);
        Assert.Single(resolved);
        Assert.Equal(new PixelRect(5,6,30,10), resolved[0].Bounds);
        Assert.Equal(graph.CaptureId, resolved[0].CaptureId);
    }
    [Fact]
    public void Resolver_namespaces_context_evidence_ids_without_collisions()
    {
        var graph = new ScreenGraphDocument(1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, 100,100,"Region",null,
            [new ScreenGraphNode("w1", ScreenNodeKind.Word, "context", new PixelRect(7,8,20,9),1,"doc",null)]);

        var resolved = EvidenceResolver.Resolve(graph, ["p:w1", "c1:w1"], "c1");

        Assert.Single(resolved);
        Assert.Equal("c1:w1", resolved[0].EvidenceId);
        Assert.Equal("w1", resolved[0].NodeId);
    }

}
