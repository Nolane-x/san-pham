using Magic.Capture.Core.History;

namespace Magic.Capture.Core.Tests;

public sealed class HistoryThumbnailPolicyTests
{
    [Fact]
    public void Pre_generate_thumbnail_for_normal_desktop_capture()
    {
        Assert.True(HistoryThumbnailPolicy.ShouldPreGenerate(3840, 2160));
    }

    [Fact]
    public void Skip_pre_generated_thumbnail_for_very_large_scrolling_capture()
    {
        Assert.False(HistoryThumbnailPolicy.ShouldPreGenerate(2_000, 20_000));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void Invalid_dimensions_do_not_request_thumbnail(int width, int height)
    {
        Assert.False(HistoryThumbnailPolicy.ShouldPreGenerate(width, height));
    }
}
