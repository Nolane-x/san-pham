using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Tests;

public sealed class AppSettingsNormalizationTests
{
    [Fact]
    public void NormalizeForRuntime_clamps_untrusted_numeric_settings()
    {
        var settings = new AppSettings
        {
            JpegQuality = 500,
            PinOpacity = double.NaN,
            HistoryMaximumAgeDays = -10,
            HistoryMaximumCount = 500_000,
            HistoryMaximumBytes = -1,
            AiCacheMaximumAgeDays = 0,
            AiCacheMaximumEntries = 1
        };

        var normalized = AppSettingsRules.NormalizeForRuntime(settings);

        Assert.Equal(100, normalized.JpegQuality);
        Assert.Equal(1.0, normalized.PinOpacity);
        Assert.Equal(0, normalized.HistoryMaximumAgeDays);
        Assert.Equal(100_000, normalized.HistoryMaximumCount);
        Assert.Null(normalized.HistoryMaximumBytes);
        Assert.Equal(1, normalized.AiCacheMaximumAgeDays);
        Assert.Equal(10, normalized.AiCacheMaximumEntries);
    }

    [Fact]
    public void NormalizeForRuntime_bounds_profiles_recent_regions_and_text()
    {
        var profiles = Enumerable.Range(0, 100)
            .Select(i => new CaptureProfile(i.ToString(), new string('x', 500), CaptureProfileSource.Region,
                new PixelRect(i, i, 100, 100), false, 999_999, PostCaptureAction.ResultWindow))
            .ToArray();
        var recent = Enumerable.Range(0, 100).Select(i => new PixelRect(i, i, 10, 10)).ToArray();
        var settings = new AppSettings
        {
            FileNameTemplate = new string('a', 10_000),
            PreferredOcrLanguage = new string('z', 500),
            CaptureProfiles = profiles,
            RecentRegions = recent
        };

        var normalized = AppSettingsRules.NormalizeForRuntime(settings);

        Assert.True(normalized.FileNameTemplate.Length <= 240);
        Assert.True(normalized.PreferredOcrLanguage!.Length <= 64);
        Assert.True(normalized.CaptureProfiles.Count <= 64);
        Assert.True(normalized.RecentRegions.Count <= 16);
        Assert.All(normalized.CaptureProfiles, profile => Assert.InRange(profile.DelayMilliseconds, 0, 60_000));
    }
    [Fact]
    public void NormalizeForRuntime_rejects_invalid_hotkeys_enums_and_default_profile_reference()
    {
        var settings = new AppSettings
        {
            RegionHotkey = new HotkeyGesture(HotkeyModifiers.None, 0x40),
            RepeatHotkey = new HotkeyGesture((HotkeyModifiers)128, 0x52),
            DefaultPostCaptureAction = (PostCaptureAction)999,
            Theme = (AppTheme)999,
            CaptureOverlayTheme = (CaptureOverlayTheme)999,
            DefaultCaptureProfileId = "missing"
        };

        var normalized = AppSettingsRules.NormalizeForRuntime(settings);

        Assert.Equal(HotkeyGesture.DefaultRegion, normalized.RegionHotkey);
        Assert.Equal(HotkeyGesture.DefaultRepeat, normalized.RepeatHotkey);
        Assert.Equal(PostCaptureAction.ResultWindow, normalized.DefaultPostCaptureAction);
        Assert.Equal(AppTheme.System, normalized.Theme);
        Assert.Equal(CaptureOverlayTheme.Dark, normalized.CaptureOverlayTheme);
        Assert.Null(normalized.DefaultCaptureProfileId);
    }


    [Fact]
    public void Normalize_bounds_privacy_patterns_words_and_preserves_redaction_policy()
    {
        var patterns = Enumerable.Range(0, 100).Select(i => new Magic.Capture.Core.Privacy.SensitivePattern(new string('L', 200), new string('x', 700))).ToArray();
        var words = Enumerable.Range(0, 100).Select(i => $" secret-{i} ").ToArray();
        var raw = new AppSettings
        {
            RedactBeforeCopy = true,
            RedactBeforeSave = true,
            RedactBeforePin = true,
            RedactBeforeWorkflow = true,
            SensitivePatterns = patterns,
            SensitiveWords = words
        };
        var normalized = AppSettingsRules.NormalizeForRuntime(raw);
        Assert.True(normalized.RedactBeforeCopy);
        Assert.True(normalized.RedactBeforeSave);
        Assert.True(normalized.RedactBeforePin);
        Assert.True(normalized.RedactBeforeWorkflow);
        Assert.True(normalized.SensitivePatterns.Count <= AppSettingsRules.MaximumSensitivePatterns);
        Assert.True(normalized.SensitiveWords.Count <= AppSettingsRules.MaximumSensitiveWords);
        Assert.All(normalized.SensitivePatterns, pattern =>
        {
            Assert.True(pattern.Label.Length <= AppSettingsRules.MaximumSensitivePatternLabelLength);
            Assert.True(pattern.Pattern.Length <= AppSettingsRules.MaximumSensitivePatternLength);
        });
        Assert.All(normalized.SensitiveWords, word => Assert.Equal(word.Trim(), word));
    }

    [Fact]
    public void Normalize_privacy_patterns_drops_overlong_regex_instead_of_mutating_it()
    {
        var valid = new Magic.Capture.Core.Privacy.SensitivePattern("Employee", @"EMP-\d{6}");
        var overlong = new Magic.Capture.Core.Privacy.SensitivePattern("Huge", new string('x', AppSettingsRules.MaximumSensitivePatternLength + 1));

        var normalized = AppSettingsRules.NormalizeForRuntime(new AppSettings { SensitivePatterns = [valid, overlong] });

        var pattern = Assert.Single(normalized.SensitivePatterns);
        Assert.Equal(valid, pattern);
    }


    [Fact]
    public void Pin_window_geometry_is_bounded()
    {
        var normalized = AppSettingsRules.NormalizeForRuntime(new AppSettings
        {
            PinLastX = int.MaxValue,
            PinLastY = int.MinValue,
            PinLastWidth = 1,
            PinLastHeight = int.MaxValue
        });
        Assert.Equal(100_000, normalized.PinLastX);
        Assert.Equal(-100_000, normalized.PinLastY);
        Assert.Equal(160, normalized.PinLastWidth);
        Assert.Equal(16_384, normalized.PinLastHeight);
    }
}
