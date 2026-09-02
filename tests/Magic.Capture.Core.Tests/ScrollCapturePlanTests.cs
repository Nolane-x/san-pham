using Magic.Capture.Core.Capture;

namespace Magic.Capture.Core.Tests;

public sealed class ScrollCapturePlanTests
{
    [Fact]
    public void GridPlan_IsRowMajorAndResetsHorizontalPositionBetweenRows()
    {
        var plan = ScrollCaptureGridPlan.Create(rows: 2, columns: 3, horizontalWheelDelta: -600, verticalWheelDelta: -720);

        Assert.Equal(6, plan.Tiles.Count);
        Assert.Equal(new ScrollCaptureTile(0, 0, 0, ScrollVector.None), plan.Tiles[0]);
        Assert.Equal(new ScrollCaptureTile(1, 0, 1, new ScrollVector(-600, 0)), plan.Tiles[1]);
        Assert.Equal(new ScrollCaptureTile(2, 0, 2, new ScrollVector(-600, 0)), plan.Tiles[2]);
        Assert.Equal(new ScrollCaptureTile(3, 1, 0, new ScrollVector(1200, -720)), plan.Tiles[3]);
        Assert.Equal(new ScrollCaptureTile(4, 1, 1, new ScrollVector(-600, 0)), plan.Tiles[4]);
        Assert.Equal(new ScrollCaptureTile(5, 1, 2, new ScrollVector(-600, 0)), plan.Tiles[5]);
    }

    [Fact]
    public void GridPlan_AllowsMaximumEightByEightGrid()
    {
        var plan = ScrollCaptureGridPlan.Create(8, 8);
        Assert.Equal(64, plan.Tiles.Count);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(9, 1)]
    [InlineData(1, 9)]
    public void GridPlan_RejectsOutOfRangeDimensions(int rows, int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrollCaptureGridPlan.Create(rows, columns));
    }

    [Fact]
    public void ScrollVector_ReportsAxisIntent()
    {
        Assert.Equal(ScrollAxis.Horizontal, new ScrollVector(-720, 0).PrimaryAxis);
        Assert.Equal(ScrollAxis.Vertical, new ScrollVector(0, -720).PrimaryAxis);
        Assert.Null(ScrollVector.None.PrimaryAxis);
    }
}
