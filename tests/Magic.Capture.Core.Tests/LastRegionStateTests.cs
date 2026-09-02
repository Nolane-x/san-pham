using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class LastRegionStateTests
{
    [Fact]
    public void Region_is_usable_when_it_intersects_current_virtual_desktop()
    {
        var state = new LastRegionState(new PixelRect(100, 100, 400, 300), "DISPLAY1", DateTimeOffset.UtcNow);
        Assert.True(state.IsUsableWithin(new PixelRect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void Region_is_not_usable_when_monitor_topology_moved_away()
    {
        var state = new LastRegionState(new PixelRect(3000, 100, 400, 300), "OLD", DateTimeOffset.UtcNow);
        Assert.False(state.IsUsableWithin(new PixelRect(0, 0, 1920, 1080)));
    }
}
