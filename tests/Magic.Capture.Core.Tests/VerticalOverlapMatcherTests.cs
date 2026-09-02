using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class VerticalOverlapMatcherTests
{
    [Fact]
    public void FindsExactSuffixPrefixOverlap()
    {
        const int width = 4;
        byte[] upper = Rows(width, 10, 20, 30, 40, 50, 60);
        byte[] lower = Rows(width, 40, 50, 60, 70, 80);

        var match = VerticalOverlapMatcher.Find(upper, 6, lower, 5, width,
            new VerticalOverlapOptions(0.2, 0.8, 0.1, 1, 1));

        Assert.NotNull(match);
        Assert.Equal(3, match!.OverlapRows);
        Assert.Equal(0, match.MeanAbsoluteDifference, 6);
    }

    [Fact]
    public void RejectsUnrelatedFrames()
    {
        const int width = 3;
        byte[] upper = Rows(width, 0, 0, 0, 0);
        byte[] lower = Rows(width, 255, 255, 255, 255);
        var match = VerticalOverlapMatcher.Find(upper, 4, lower, 4, width,
            new VerticalOverlapOptions(0.25, 0.75, 5, 1, 1));
        Assert.Null(match);
    }


    [Fact]
    public void FindsOverlapAfterTrimmingStickyHeaderAndFooter()
    {
        const int width = 3;
        byte[] upper = Rows(width, 1, 10, 20, 30, 99);
        byte[] lower = Rows(width, 1, 20, 30, 40, 99);

        var match = VerticalOverlapMatcher.FindTrimmed(
            upper, 5, lower, 5, width,
            upperTopRows: 0, upperBottomRows: 1,
            lowerTopRows: 1, lowerBottomRows: 0,
            new VerticalOverlapOptions(0.2, 0.9, 0.1, 1, 1));

        Assert.NotNull(match);
        Assert.Equal(2, match!.OverlapRows);
        Assert.Equal(0, match.MeanAbsoluteDifference, 6);
    }

    [Fact]
    public void TrimmedMatcherRejectsInvalidTrimGeometry()
    {
        const int width = 2;
        var frame = Rows(width, 1, 2, 3);
        var match = VerticalOverlapMatcher.FindTrimmed(
            frame, 3, frame, 3, width,
            upperTopRows: 2, upperBottomRows: 2,
            lowerTopRows: 0, lowerBottomRows: 0);

        Assert.Null(match);
    }

    private static byte[] Rows(int width, params byte[] rowValues) =>
        rowValues.SelectMany(v => Enumerable.Repeat(v, width)).ToArray();
}
