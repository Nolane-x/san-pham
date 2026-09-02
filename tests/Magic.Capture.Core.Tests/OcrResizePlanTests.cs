using Magic.Capture.Core.Ocr;

namespace Magic.Capture.Core.Tests;

public sealed class OcrResizePlanTests
{
    [Fact]
    public void Leaves_images_within_engine_limit_unchanged()
    {
        var plan = OcrResizePlan.Create(1920, 1080, 7680);

        Assert.False(plan.RequiresResize);
        Assert.Equal(1920, plan.TargetWidth);
        Assert.Equal(1080, plan.TargetHeight);
        Assert.Equal(1d, plan.ScaleXToOriginal);
        Assert.Equal(1d, plan.ScaleYToOriginal);
    }

    [Fact]
    public void Downscales_long_edge_and_preserves_aspect_ratio()
    {
        var plan = OcrResizePlan.Create(10000, 5000, 7680);

        Assert.True(plan.RequiresResize);
        Assert.Equal(7680, plan.TargetWidth);
        Assert.Equal(3840, plan.TargetHeight);
        Assert.InRange(plan.ScaleXToOriginal, 1.3020, 1.3022);
        Assert.InRange(plan.ScaleYToOriginal, 1.3020, 1.3022);
    }

    [Theory]
    [InlineData(0, 100, 7680)]
    [InlineData(100, 0, 7680)]
    [InlineData(100, 100, 0)]
    public void Rejects_invalid_dimensions(int width, int height, int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OcrResizePlan.Create(width, height, maximum));
    }
}
