using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureProfileTests
{
    [Fact]
    public void Normalize_region_clamps_to_desktop_bounds()
    {
        var desktop = new PixelRect(-1920, 0, 3840, 1080);
        var requested = new PixelRect(-2000, -10, 400, 300);
        var normalized = CaptureRegionRules.Normalize(requested, desktop);
        Assert.Equal(new PixelRect(-1920, 0, 320, 290), normalized);
    }

    [Fact]
    public void Recent_regions_deduplicates_and_keeps_newest_first()
    {
        var a = new PixelRect(1, 2, 100, 80);
        var b = new PixelRect(5, 6, 200, 160);
        IReadOnlyList<PixelRect> recent = [];
        recent = RecentCaptureRegions.Push(recent, a, 3);
        recent = RecentCaptureRegions.Push(recent, b, 3);
        recent = RecentCaptureRegions.Push(recent, a, 3);
        Assert.Equal([a, b], recent);
    }

    [Fact]
    public void Capture_profile_can_bind_region_and_workflow_without_platform_types()
    {
        var profile = new CaptureProfile(
            "docs", "Documentation", CaptureProfileSource.Region,
            new PixelRect(10, 20, 1280, 720), true, 250,
            PostCaptureAction.ResultWindow, "documentation", "png");
        Assert.Equal("documentation", profile.WorkflowId);
        Assert.Equal(1280, profile.Region!.Value.Width);
    }
}
