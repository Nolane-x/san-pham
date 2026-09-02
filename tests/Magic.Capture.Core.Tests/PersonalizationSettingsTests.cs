using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Settings;
using Xunit;

namespace Magic.Capture.Core.Tests;

public sealed class PersonalizationSettingsTests
{
    [Fact]
    public void NormalizeForRuntime_BoundsPersonalizationCollections()
    {
        var profiles = new[] { new CaptureProfile("p", "P", CaptureProfileSource.ForegroundWindow) };
        var settings = new AppSettings
        {
            CaptureProfiles = profiles,
            AnnotationStylePresets = Enumerable.Range(0, 40).Select(i => new AnnotationStylePreset($"s{i}", $"Style {i}")).ToArray(),
            MonitorPreferences = Enumerable.Range(0, 40).Select(i => new MonitorCapturePreference($"DISPLAY{i}")).ToArray(),
            AppCaptureRules = Enumerable.Range(0, 80).Select(i => new AppCaptureRule($"a{i}", $"app{i}.exe", "p")).ToArray()
        };
        var normalized = AppSettingsRules.NormalizeForRuntime(settings);
        Assert.Equal(AppSettingsRules.MaximumAnnotationStylePresets, normalized.AnnotationStylePresets.Count);
        Assert.Equal(AppSettingsRules.MaximumMonitorPreferences, normalized.MonitorPreferences.Count);
        Assert.Equal(AppSettingsRules.MaximumAppCaptureRules, normalized.AppCaptureRules.Count);
    }

    [Fact]
    public void NormalizeForRuntime_RemovesDuplicatePersonalHotkeys()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4D);
        var settings = new AppSettings
        {
            PersonalHotkeys =
            [
                new("one", "One", PersonalHotkeyKind.Capture, "ActiveMonitor", gesture),
                new("two", "Two", PersonalHotkeyKind.Editor, "open-last", gesture)
            ]
        };
        var normalized = AppSettingsRules.NormalizeForRuntime(settings);
        Assert.Single(normalized.PersonalHotkeys);
    }

    [Fact]
    public void ResetSection_PreservesUnrelatedSettings()
    {
        var settings = new AppSettings
        {
            Theme = AppTheme.Dark,
            DefaultAnnotationTool = AnnotationKind.Arrow,
            PersonalHotkeys = [new("x", "X", PersonalHotkeyKind.Editor, "open-last", new(HotkeyModifiers.Control, 0x45))]
        };
        var reset = AppSettingsRules.ResetSection(settings, SettingsSection.Personalization);
        Assert.Equal(AppTheme.Dark, reset.Theme);
        Assert.Single(reset.PersonalHotkeys);
        Assert.Equal(AnnotationKind.Rectangle, reset.DefaultAnnotationTool);
    }

    [Fact]
    public void NormalizeForRuntime_AcceptsCaptureProfileHotkeyTarget()
    {
        var profile = new Magic.Capture.Core.Capture.CaptureProfile("scrolling", "Scrolling", Magic.Capture.Core.Capture.CaptureProfileSource.Scrolling);
        var settings = new AppSettings
        {
            CaptureProfiles = [profile],
            PersonalHotkeys = [new PersonalHotkeyBinding("profile-hotkey", "Scrolling", PersonalHotkeyKind.Capture, "profile:scrolling", new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x53))]
        };

        var normalized = AppSettingsRules.NormalizeForRuntime(settings);

        Assert.Single(normalized.PersonalHotkeys);
        Assert.Equal("profile:scrolling", normalized.PersonalHotkeys[0].Target);
    }
}
