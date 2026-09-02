using Magic.Capture.Core.Capture;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureSizePresetTests
{
    [Fact]
    public void BuiltIn_presets_are_unique_positive_and_bounded()
    {
        Assert.NotEmpty(CaptureSizePresets.BuiltIn);
        Assert.Equal(CaptureSizePresets.BuiltIn.Count, CaptureSizePresets.BuiltIn.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CaptureSizePresets.BuiltIn, item =>
        {
            Assert.InRange(item.Width, 1, 7680);
            Assert.InRange(item.Height, 1, 7680);
        });
    }

    [Theory]
    [InlineData("720p", 1280, 720)]
    [InlineData("1080p", 1920, 1080)]
    [InlineData("1440p", 2560, 1440)]
    [InlineData("4k", 3840, 2160)]
    [InlineData("square-1080", 1080, 1080)]
    [InlineData("social-portrait", 1080, 1350)]
    public void BuiltIn_contains_expected_desktop_and_social_sizes(string id, int width, int height)
    {
        var preset = CaptureSizePresets.BuiltIn.Single(item => item.Id == id);
        Assert.Equal(width, preset.Width);
        Assert.Equal(height, preset.Height);
    }
}
