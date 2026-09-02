using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class StableEdgeBandDetectorTests
{
    [Fact]
    public void DetectsStableTopAndBottomBandsWhenBodyMoves()
    {
        const int width = 24;
        const int height = 20;
        var first = SolidBgra(width, height, 10);
        var second = SolidBgra(width, height, 10);

        // Body content changes while a 3-row header and 2-row footer stay fixed.
        FillRows(second, width, 3, height - 2, 220);

        var result = StableEdgeBandDetector.Detect(
            first, second, width, height,
            new StableEdgeBandOptions(
                MaximumBandRatio: 0.40,
                MinimumBandRows: 2,
                MaximumRowChangedPercent: 5,
                MinimumGlobalChangedPercent: 20,
                SampleEveryColumns: 1,
                ChannelThreshold: 8));

        Assert.Equal(3, result.TopRows);
        Assert.Equal(2, result.BottomRows);
    }

    [Fact]
    public void DoesNotInventStickyBandsWhenFrameIsEffectivelyStatic()
    {
        const int width = 12;
        const int height = 12;
        var first = SolidBgra(width, height, 30);
        var second = SolidBgra(width, height, 30);

        var result = StableEdgeBandDetector.Detect(
            first, second, width, height,
            new StableEdgeBandOptions(MinimumBandRows: 2, MinimumGlobalChangedPercent: 5, SampleEveryColumns: 1));

        Assert.Equal(0, result.TopRows);
        Assert.Equal(0, result.BottomRows);
    }

    [Fact]
    public void RejectsBuffersThatDoNotMatchGeometry()
    {
        Assert.Throws<ArgumentException>(() =>
            StableEdgeBandDetector.Detect(new byte[8], new byte[8], 4, 4));
    }

    private static byte[] SolidBgra(int width, int height, byte value)
    {
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = value;
            bytes[i + 1] = value;
            bytes[i + 2] = value;
            bytes[i + 3] = 255;
        }
        return bytes;
    }

    private static void FillRows(byte[] bgra, int width, int startRow, int endRowExclusive, byte value)
    {
        for (var y = startRow; y < endRowExclusive; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            bgra[offset] = value;
            bgra[offset + 1] = value;
            bgra[offset + 2] = value;
        }
    }
}
