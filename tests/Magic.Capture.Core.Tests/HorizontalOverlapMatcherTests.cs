using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class HorizontalOverlapMatcherTests
{
    [Fact]
    public void FindsExactRightLeftOverlap()
    {
        const int height = 3;
        var left = Columns(height, 10, 20, 30, 40, 50, 60);
        var right = Columns(height, 40, 50, 60, 70, 80);

        var match = HorizontalOverlapMatcher.Find(left, 6, right, 5, height,
            new HorizontalOverlapOptions(0.2, 0.8, 0.1, 1, 1));

        Assert.NotNull(match);
        Assert.Equal(3, match!.OverlapColumns);
        Assert.Equal(0, match.MeanAbsoluteDifference, 6);
    }

    [Fact]
    public void RejectsUnrelatedFrames()
    {
        const int height = 4;
        var left = Columns(height, 0, 0, 0, 0);
        var right = Columns(height, 255, 255, 255, 255);

        var match = HorizontalOverlapMatcher.Find(left, 4, right, 4, height,
            new HorizontalOverlapOptions(0.25, 0.75, 5, 1, 1));

        Assert.Null(match);
    }

    private static byte[] Columns(int height, params byte[] columnValues)
    {
        var width = columnValues.Length;
        var data = new byte[checked(width * height)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                data[y * width + x] = columnValues[x];
        return data;
    }
}
