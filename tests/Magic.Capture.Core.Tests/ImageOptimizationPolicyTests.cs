using Magic.Capture.Core.Export;

namespace Magic.Capture.Core.Tests;

public sealed class ImageOptimizationPolicyTests
{
    [Fact]
    public void NormalizesTargetAndQualityBounds()
    {
        var policy = new ImageOptimizationPolicy(TargetBytes: 1, JpegQuality: 500, MinimumJpegQuality: -4, MaxDimension: 999999).Normalize();
        Assert.Equal(16 * 1024, policy.TargetBytes);
        Assert.Equal(100, policy.JpegQuality);
        Assert.Equal(1, policy.MinimumJpegQuality);
        Assert.Equal(32768, policy.MaxDimension);
    }

    [Fact]
    public void ComputesBoundedResizeScaleWhenPayloadMissesTarget()
    {
        var scale = ImageOptimizationPolicy.ResizeScale(currentBytes: 4_000_000, targetBytes: 1_000_000);
        Assert.InRange(scale, 0.45, 0.55);
        Assert.Equal(1.0, ImageOptimizationPolicy.ResizeScale(500_000, 1_000_000), 6);
    }
}
