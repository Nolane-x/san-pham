using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class BitmapStridePolicyTests
{
    [Theory]
    [InlineData(0, 16, 0)]
    [InlineData(1, 16, 16)]
    [InlineData(2, 16, 32)]
    [InlineData(0, -16, 0)]
    [InlineData(1, -16, -16)]
    [InlineData(2, -16, -32)]
    public void RowOffset_FollowsSignedStrideFromScan0(int row, int stride, int expected)
    {
        Assert.Equal(expected, BitmapStridePolicy.RowOffset(row, stride));
    }

    [Fact]
    public void RowOffset_RejectsNegativeRows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapStridePolicy.RowOffset(-1, 16));
    }
}
