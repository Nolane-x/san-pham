using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class FrameDifferenceTests
{
    [Fact]
    public void Sampled_change_ignores_alpha_and_reports_rgb_change_percentage()
    {
        byte[] a = [0, 0, 0, 0, 10, 10, 10, 255, 20, 20, 20, 255, 30, 30, 30, 255];
        byte[] b = [0, 0, 0, 255, 10, 10, 10, 0, 200, 200, 200, 255, 30, 30, 30, 255];
        var percent = FrameDifference.SampledChangedPercent(a, b, sampleEveryPixels: 1, channelThreshold: 8);
        Assert.Equal(25, percent, 8);
    }

    [Fact]
    public void Sampled_change_can_stride_for_lightweight_long_capture_detection()
    {
        var a = new byte[40 * 4];
        var b = new byte[a.Length];
        b[20 * 4 + 2] = 255;
        Assert.Equal(0, FrameDifference.SampledChangedPercent(a, b, sampleEveryPixels: 8, channelThreshold: 8));
        Assert.True(FrameDifference.SampledChangedPercent(a, b, sampleEveryPixels: 4, channelThreshold: 8) > 0);
    }
}
