using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class ImageDifferenceTests
{
    [Fact]
    public void Threshold_controls_changed_pixel_count_and_reports_per_channel_means()
    {
        byte[] a = [10, 20, 30, 255, 0, 0, 0, 255];
        byte[] b = [14, 26, 39, 255, 40, 50, 60, 255];
        var result = ImageDifference.AnalyzeBgra(a, b, new ImageDifferenceOptions(Threshold: 10));
        Assert.Equal(1, result.ChangedPixelCount);
        Assert.Equal(2, result.ComparedPixelCount);
        Assert.Equal(22, result.MeanBlueDifference, 6);
        Assert.Equal(28, result.MeanGreenDifference, 6);
        Assert.Equal(34.5, result.MeanRedDifference, 6);
    }

    [Fact]
    public void Ignore_fully_transparent_excludes_pixels_from_statistics()
    {
        byte[] a = [0, 0, 0, 0, 10, 20, 30, 255];
        byte[] b = [255, 255, 255, 0, 10, 20, 30, 255];
        var ignored = ImageDifference.AnalyzeBgra(a, b, new ImageDifferenceOptions(IgnoreFullyTransparent: true));
        Assert.Equal(1, ignored.ComparedPixelCount);
        Assert.Equal(0, ignored.ChangedPixelCount);
        var included = ImageDifference.AnalyzeBgra(a, b, new ImageDifferenceOptions(IgnoreFullyTransparent: false));
        Assert.Equal(2, included.ComparedPixelCount);
        Assert.Equal(1, included.ChangedPixelCount);
    }

    [Fact]
    public void Alpha_can_be_ignored_or_included_for_change_classification()
    {
        byte[] a = [10, 20, 30, 0];
        byte[] b = [10, 20, 30, 255];
        Assert.Equal(0, ImageDifference.AnalyzeBgra(a, b, new ImageDifferenceOptions(IgnoreAlpha: true, IgnoreFullyTransparent: false)).ChangedPixelCount);
        Assert.Equal(1, ImageDifference.AnalyzeBgra(a, b, new ImageDifferenceOptions(IgnoreAlpha: false, IgnoreFullyTransparent: false)).ChangedPixelCount);
    }
}
