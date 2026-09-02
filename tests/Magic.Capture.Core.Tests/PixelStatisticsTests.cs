using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class PixelStatisticsTests
{
    [Fact]
    public void ComputesChannelMeansAndOpaqueCoverageFromBgra()
    {
        byte[] pixels =
        [
            10, 20, 30, 255,
            30, 40, 50, 128,
        ];

        var stats = PixelStatistics.ComputeBgra(pixels, width: 2, height: 1);

        Assert.Equal(40, stats.MeanRed, 6);
        Assert.Equal(30, stats.MeanGreen, 6);
        Assert.Equal(20, stats.MeanBlue, 6);
        Assert.Equal(191.5, stats.MeanAlpha, 6);
        Assert.Equal(50, stats.OpaquePixelPercent, 6);
    }

    [Fact]
    public void RejectsPixelBufferWithWrongLength()
    {
        Assert.Throws<ArgumentException>(() => PixelStatistics.ComputeBgra([1, 2, 3], 1, 1));
    }
}
