using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class WorkflowTriggerTests
{
    [Fact]
    public void Validator_accepts_bounded_schedule_and_rejects_duplicate_hotkeys()
    {
        var schedule = new WorkflowTrigger("a", "Morning", WorkflowTriggerKind.Schedule, "profile", "quick-copy", Schedule: new("08:30", WorkflowTriggerDays.Weekdays));
        Assert.True(WorkflowTriggerPolicy.Validate(schedule).IsValid);

        var hotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4B);
        var triggers = new[]
        {
            new WorkflowTrigger("b", "One", WorkflowTriggerKind.Hotkey, "profile", "quick-copy", Hotkey: hotkey),
            new WorkflowTrigger("c", "Two", WorkflowTriggerKind.Hotkey, "profile", "quick-copy", Hotkey: hotkey)
        };
        Assert.False(WorkflowTriggerPolicy.ValidateSet(triggers).IsValid);
    }

    [Fact]
    public void Unattended_policy_rejects_interactive_region_and_scrolling()
    {
        Assert.False(WorkflowTriggerPolicy.IsCaptureProfileUnattendedSafe(new CaptureProfile("a", "Region", CaptureProfileSource.Region)));
        Assert.True(WorkflowTriggerPolicy.IsCaptureProfileUnattendedSafe(new CaptureProfile("b", "Exact", CaptureProfileSource.Region, new PixelRect(10, 20, 300, 200))));
        Assert.False(WorkflowTriggerPolicy.IsCaptureProfileUnattendedSafe(new CaptureProfile("c", "Scroll", CaptureProfileSource.Scrolling)));
    }
    [Fact]
    public void Validator_uses_windows_local_paths_and_safe_identifier_rules_cross_platform()
    {
        var file = new WorkflowTrigger(
            "file-1",
            "Files",
            WorkflowTriggerKind.FileChange,
            "profile",
            "quick-copy",
            FileChange: new WorkflowTriggerFileChange(@"C:\Users\Example\Pictures", "*.png"));

        Assert.True(WorkflowTriggerPolicy.Validate(file).IsValid);
        Assert.True(WorkflowTriggerPolicy.IsSafeIdentifier("trigger_1.a-b"));
        Assert.False(WorkflowTriggerPolicy.IsSafeIdentifier("bad trigger"));
        Assert.False(WorkflowTriggerPolicy.Validate(file with
        {
            FileChange = new WorkflowTriggerFileChange(@"\\server\share", "*.png")
        }).IsValid);
        Assert.False(WorkflowTriggerPolicy.Validate(file with
        {
            FileChange = new WorkflowTriggerFileChange(@"C:\Users\Example", @"nested\*.png")
        }).IsValid);
    }

    [Fact]
    public void Validator_restricts_hotkeys_to_supported_keys()
    {
        Assert.True(WorkflowTriggerPolicy.IsValidHotkey(new HotkeyGesture(HotkeyModifiers.Control, 0x41)));
        Assert.True(WorkflowTriggerPolicy.IsValidHotkey(new HotkeyGesture(HotkeyModifiers.Control, 0x70)));
        Assert.False(WorkflowTriggerPolicy.IsValidHotkey(new HotkeyGesture(HotkeyModifiers.Control, 0x25)));
    }

}
