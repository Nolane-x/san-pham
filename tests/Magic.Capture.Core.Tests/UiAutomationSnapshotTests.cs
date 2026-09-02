using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class UiAutomationSnapshotTests
{
    [Fact]
    public void Normalize_DeduplicatesKeysBoundsStringsAndNodeCount()
    {
        var nodes = Enumerable.Range(0, UiAutomationSnapshotRules.MaximumNodes + 40)
            .Select(i => Node($"node-{i}", null, new PixelRect(i, 0, 20, 20), z: i % 3, depth: 1,
                name: new string('N', 900)))
            .Prepend(Node("node-0", null, new PixelRect(1, 1, 10, 10), z: 0, depth: 1));

        var snapshot = UiAutomationSnapshotRules.Normalize(nodes, wasTruncated: true);

        Assert.Equal(UiAutomationSnapshotRules.MaximumNodes, snapshot.Nodes.Count);
        Assert.Equal(snapshot.Nodes.Count, snapshot.Nodes.Select(node => node.StableKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(snapshot.Nodes, node => Assert.InRange(node.Name?.Length ?? 0, 0, UiAutomationSnapshotRules.MaximumTextLength));
        Assert.True(snapshot.WasTruncated);
    }

    [Fact]
    public void FindSnapTarget_PrefersTopmostWindowBeforeSmallerObscuredControl()
    {
        var snapshot = UiAutomationSnapshotRules.Normalize([
            Node("front-window", null, new PixelRect(0, 0, 300, 300), z: 0, depth: 0, type: "Window"),
            Node("front-button", "front-window", new PixelRect(20, 20, 180, 70), z: 0, depth: 1, type: "Button"),
            Node("back-window", null, new PixelRect(0, 0, 300, 300), z: 1, depth: 0, type: "Window"),
            Node("back-tiny", "back-window", new PixelRect(30, 30, 20, 20), z: 1, depth: 4, type: "Button")
        ], wasTruncated: false);

        var target = UiAutomationSnapshotRules.FindSnapTarget(snapshot, new PixelPoint(35, 35));

        Assert.NotNull(target);
        Assert.Equal("front-button", target!.StableKey);
    }

    [Fact]
    public void ProjectSnapTargets_UsesMonitorLocalPhysicalCoordinates()
    {
        var snapshot = UiAutomationSnapshotRules.Normalize([
            Node("button", null, new PixelRect(-1870, 120, 100, 40), z: 0, depth: 2, type: "Button", name: "Submit")
        ], false);
        var monitor = new PixelRect(-1920, 0, 1920, 1080);

        var targets = UiAutomationSnapshotRules.ProjectSnapTargets(snapshot, monitor);

        var target = Assert.Single(targets);
        Assert.Equal(new PixelRect(50, 120, 100, 40), target.Bounds);
        Assert.Equal("Button · Submit", target.Label);
    }

    [Fact]
    public void ProjectForCapture_KeepsAcceptedAncestorsAndTranslatesToImageCoordinates()
    {
        var snapshot = UiAutomationSnapshotRules.Normalize([
            Node("window", null, new PixelRect(100, 100, 800, 600), z: 0, depth: 0, type: "Window"),
            Node("panel", "window", new PixelRect(180, 160, 500, 300), z: 0, depth: 1, type: "Pane"),
            Node("submit", "panel", new PixelRect(220, 200, 120, 40), z: 0, depth: 2, type: "Button", name: "Submit")
        ], false);
        var monitor = new PixelRect(0, 0, 1920, 1080);
        Assert.True(CaptureSelectionGeometryRules.TryCreateBox(
            CaptureSelectionKind.Rectangle, new PixelRect(210, 190, 150, 80),
            new PixelRect(0, 0, 1920, 1080), out var selection, out _));

        var projected = UiAutomationSnapshotRules.ProjectForCapture(snapshot, monitor, selection!);

        Assert.Equal(3, projected.Count);
        var button = projected.Single(node => node.StableKey == "submit");
        Assert.Equal(new PixelRect(10, 10, 120, 40), button.Bounds);
        Assert.Equal("panel", button.ParentStableKey);
        Assert.Contains(projected, node => node.StableKey == "window");
    }

    [Fact]
    public void ProjectForCapture_FiltersControlsOutsideEllipseMask()
    {
        var snapshot = UiAutomationSnapshotRules.Normalize([
            Node("window", null, new PixelRect(0, 0, 100, 100), z: 0, depth: 0, type: "Window"),
            Node("corner", "window", new PixelRect(0, 0, 10, 10), z: 0, depth: 1, type: "Button"),
            Node("center", "window", new PixelRect(45, 45, 10, 10), z: 0, depth: 1, type: "Button")
        ], false);
        Assert.True(CaptureSelectionGeometryRules.TryCreateBox(
            CaptureSelectionKind.Ellipse, new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 100),
            out var selection, out _));

        var projected = UiAutomationSnapshotRules.ProjectForCapture(snapshot, new PixelRect(0, 0, 100, 100), selection!);

        Assert.DoesNotContain(projected, node => node.StableKey == "corner");
        Assert.Contains(projected, node => node.StableKey == "center");
        Assert.Contains(projected, node => node.StableKey == "window");
    }

    [Fact]
    public void Normalize_strips_password_values_at_the_core_boundary()
    {
        var password = new UiAutomationSnapshotNode(
            "pwd", null, "Edit", "Password", "password", "secret-value", true, null, null, true,
            new PixelRect(10, 10, 120, 30), null, null, 123, "app", "Window", 0, 1, true);

        var snapshot = UiAutomationSnapshotRules.Normalize([password], false);

        Assert.Null(Assert.Single(snapshot.Nodes).Value);
    }


    private static UiAutomationSnapshotNode Node(
        string key,
        string? parent,
        PixelRect bounds,
        int z,
        int depth,
        string type = "Pane",
        string? name = null) => new(
            key,
            parent,
            type,
            name,
            null,
            null,
            true,
            null,
            null,
            false,
            bounds,
            null,
            null,
            123,
            "app",
            "Window",
            z,
            depth);
}
