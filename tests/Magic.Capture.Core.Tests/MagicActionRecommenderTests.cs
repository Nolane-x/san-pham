using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Tests;

public sealed class MagicActionRecommenderTests
{
    [Fact]
    public void Error_graph_prioritizes_error_actions()
    {
        var graph = Graph(
            Node("s1", ScreenNodeKind.Error, "NullReferenceException"),
            Node("s2", ScreenNodeKind.StackFrame, "at App.Run() in Main.cs:42"));

        var recommendations = MagicActionRecommender.Recommend(graph);

        Assert.Equal("developer.explain-error", recommendations[0].ActionId);
        Assert.Contains(recommendations.Take(4), x => x.ActionId == "developer.bug-report");
        Assert.Contains(recommendations.Take(4), x => x.ActionId == "developer.stack-trace");
    }

    [Fact]
    public void Table_graph_prioritizes_data_actions()
    {
        var graph = Graph(Node("t1", ScreenNodeKind.Table, null));

        var recommendations = MagicActionRecommender.Recommend(graph);

        Assert.Equal("data.explain-table", recommendations[0].ActionId);
        Assert.Contains(recommendations.Take(4), x => x.ActionId == "data.records");
    }

    [Fact]
    public void Generic_text_still_has_safe_general_recommendations()
    {
        var graph = Graph(Node("l1", ScreenNodeKind.TextLine, "Quarterly meeting notes"));

        var recommendations = MagicActionRecommender.Recommend(graph);

        Assert.Contains(recommendations.Take(3), x => x.ActionId == "general.summarize");
        Assert.Contains(recommendations.Take(4), x => x.ActionId == "general.explain");
    }

    private static ScreenGraphDocument Graph(params ScreenGraphNode[] nodes) =>
        new(1, Guid.NewGuid(), DateTimeOffset.UtcNow, 800, 600, "Region", "Test", nodes);

    private static ScreenGraphNode Node(string id, ScreenNodeKind kind, string? text) =>
        new(id, kind, text, new PixelRect(0, 0, 100, 20), .95, "doc", null);
}
