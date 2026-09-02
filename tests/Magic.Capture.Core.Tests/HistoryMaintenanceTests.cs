using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryMaintenanceTests
{
    [Fact]
    public void Plan_treats_primary_png_as_source_of_truth_and_derived_state_as_rebuildable()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var missing = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var item = new HistoryItem(id, DateTimeOffset.UtcNow, $"2026/08/24/{id:N}.png", 100, 50, "Region", null, null, 100,
            $"2026/08/24/{id:N}.thumb.png");
        var missingItem = new HistoryItem(missing, DateTimeOffset.UtcNow, $"2026/08/24/{missing:N}.png", 100, 50, "Region", null, null, 100);
        var orphan = "2026/08/24/33333333333333333333333333333333.png";
        var orphanThumb = "2026/08/24/44444444444444444444444444444444.thumb.png";

        var plan = HistoryMaintenance.Plan(
            [item, missingItem],
            [item.RelativePath, orphan],
            [],
            [orphanThumb]);

        Assert.Contains(missing, plan.RowsWithoutPrimary);
        Assert.Contains(orphan, plan.OrphanPrimaryPaths);
        Assert.Contains(id, plan.MissingThumbnailItemIds);
        Assert.Contains(orphanThumb, plan.OrphanThumbnailPaths);
        Assert.Contains(id, plan.MissingFingerprintItemIds);
    }
}
