using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void DefaultFileNameUsesOfficialProductBrandSpacing()
    {
        var settings = new AppSettings();
        Assert.StartsWith("Magic Capture Desktop_", settings.FileNameTemplate);
        Assert.DoesNotContain("Magic" + "Capture", settings.FileNameTemplate);
    }

    [Fact]
    public void DefaultsToResultWindowAndExposesAllImmediatePostCaptureActions()
    {
        var settings = new AppSettings();
        Assert.Equal(PostCaptureAction.ResultWindow, settings.DefaultPostCaptureAction);
        Assert.Contains(PostCaptureAction.CopyImage, Enum.GetValues<PostCaptureAction>());
        Assert.Contains(PostCaptureAction.PinImage, Enum.GetValues<PostCaptureAction>());
        Assert.Contains(PostCaptureAction.Save, Enum.GetValues<PostCaptureAction>());
    }

    [Fact]
    public void Ai_cache_defaults_are_local_and_bounded()
    {
        var settings = new AppSettings();
        Assert.True(settings.EnableAiResultCache);
        Assert.InRange(settings.AiCacheMaximumAgeDays, 1, 365);
        Assert.InRange(settings.AiCacheMaximumEntries, 10, 5000);
    }
}

