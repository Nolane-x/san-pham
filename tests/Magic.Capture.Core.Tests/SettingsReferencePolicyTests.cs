using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Settings;
using Xunit;

namespace Magic.Capture.Core.Tests;

public sealed class SettingsReferencePolicyTests
{
    [Fact]
    public void RemoveWorkflowReferences_CleansDefaultProfilesAndBindings()
    {
        var settings = new AppSettings
        {
            DefaultWorkflowId = "gone",
            CaptureProfiles = [new CaptureProfile("p", "P", CaptureProfileSource.ActiveMonitor, null, false, 0, PostCaptureAction.ResultWindow, "gone", "png")],
            PersonalHotkeys = [new PersonalHotkeyBinding("h", "H", PersonalHotkeyKind.Workflow, "gone", HotkeyGesture.DefaultRegion)]
        };

        var result = SettingsReferencePolicy.RemoveWorkflowReferences(settings, "gone");

        Assert.Null(result.DefaultWorkflowId);
        Assert.Null(result.CaptureProfiles.Single().WorkflowId);
        Assert.Empty(result.PersonalHotkeys);
    }

    [Fact]
    public void RemoveCaptureProfileReferences_CleansRulesBindingsAndDefault()
    {
        var settings = new AppSettings
        {
            CaptureProfiles = [new CaptureProfile("p", "P", CaptureProfileSource.ActiveMonitor, null, false, 0, PostCaptureAction.ResultWindow, null, "png")],
            DefaultCaptureProfileId = "p",
            AppCaptureRules = [new AppCaptureRule("r", "app.exe", "p")],
            PersonalHotkeys = [new PersonalHotkeyBinding("h", "H", PersonalHotkeyKind.Capture, "profile:p", HotkeyGesture.DefaultRegion)]
        };

        var result = SettingsReferencePolicy.RemoveCaptureProfileReferences(settings, "p");

        Assert.Empty(result.CaptureProfiles);
        Assert.Null(result.DefaultCaptureProfileId);
        Assert.Empty(result.AppCaptureRules);
        Assert.Empty(result.PersonalHotkeys);
    }
    [Fact]
    public void RequiresExternalReferencePrune_DetectsDanglingWorkflowAndProfileHotkeys()
    {
        var settings = new AppSettings
        {
            DefaultWorkflowId = "gone-workflow",
            CaptureProfiles = [new CaptureProfile("profile-a", "Profile A", CaptureProfileSource.Region)],
            PersonalHotkeys =
            [
                new PersonalHotkeyBinding("workflow-hotkey", "Workflow", PersonalHotkeyKind.Workflow, "gone-workflow", new HotkeyGesture(HotkeyModifiers.Control, 0x31)),
                new PersonalHotkeyBinding("profile-hotkey", "Profile", PersonalHotkeyKind.Capture, "profile:missing", new HotkeyGesture(HotkeyModifiers.Control, 0x32))
            ]
        };

        Assert.True(SettingsReferencePolicy.RequiresExternalReferencePrune(
            settings,
            new HashSet<string>(StringComparer.Ordinal) { "valid-workflow" },
            new HashSet<string>(StringComparer.Ordinal) { "general.explain" }));
    }

    [Fact]
    public void RequiresExternalReferencePrune_ReturnsFalseForValidReferences()
    {
        var settings = new AppSettings
        {
            DefaultWorkflowId = "workflow-a",
            CaptureProfiles = [new CaptureProfile("profile-a", "Profile A", CaptureProfileSource.Region, WorkflowId: "workflow-a")],
            PersonalHotkeys =
            [
                new PersonalHotkeyBinding("workflow-hotkey", "Workflow", PersonalHotkeyKind.Workflow, "workflow-a", new HotkeyGesture(HotkeyModifiers.Control, 0x31)),
                new PersonalHotkeyBinding("profile-hotkey", "Profile", PersonalHotkeyKind.Capture, "profile:profile-a", new HotkeyGesture(HotkeyModifiers.Control, 0x32))
            ]
        };

        Assert.False(SettingsReferencePolicy.RequiresExternalReferencePrune(
            settings,
            new HashSet<string>(StringComparer.Ordinal) { "workflow-a" },
            new HashSet<string>(StringComparer.Ordinal) { "general.explain" }));
    }

}
