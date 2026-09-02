using System.Globalization;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Workflows;

public static class WorkflowTriggerPolicy
{
    public const int MaximumTriggers = 64;
    public const int MaximumHotkeyTriggers = 16;
    public const int MinimumCooldownSeconds = 1;
    public const int MaximumCooldownSeconds = 3600;
    public const int CircuitBreakerMaximumRuns = 20;
    public static readonly TimeSpan CircuitBreakerWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CircuitBreakerSuspension = TimeSpan.FromMinutes(10);
    public const int MaximumNameLength = 120;
    public const int MaximumIdentifierLength = 128;
    public const int MaximumPatternLength = 240;
    public const int MaximumFilterLength = 120;

    public static WorkflowTriggerValidationResult ValidateSet(IReadOnlyList<WorkflowTrigger>? triggers)
    {
        triggers ??= [];
        var errors = new List<string>();
        if (triggers.Count > MaximumTriggers) errors.Add($"No more than {MaximumTriggers} triggers are allowed.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hotkeys = new HashSet<string>(StringComparer.Ordinal);
        var hotkeyCount = 0;
        foreach (var trigger in triggers)
        {
            foreach (var error in Validate(trigger).Errors) errors.Add($"{trigger?.Name ?? "Trigger"}: {error}");
            if (trigger is null) continue;
            if (!ids.Add(trigger.Id.Trim())) errors.Add($"Duplicate trigger id: {trigger.Id}");
            if (trigger.Kind == WorkflowTriggerKind.Hotkey && trigger.Hotkey is { } gesture)
            {
                hotkeyCount++;
                var key = $"{(int)gesture.Modifiers}:{gesture.VirtualKey}";
                if (!hotkeys.Add(key)) errors.Add("Workflow trigger hotkeys must be unique.");
            }
        }
        if (hotkeyCount > MaximumHotkeyTriggers) errors.Add($"No more than {MaximumHotkeyTriggers} hotkey triggers are allowed.");
        return new(errors.Count == 0, errors);
    }

    public static WorkflowTriggerValidationResult Validate(WorkflowTrigger? trigger)
    {
        var errors = new List<string>();
        if (trigger is null) return new(false, ["Trigger is required."]);
        if (!IsSafeIdentifier(trigger.Id)) errors.Add("Trigger id is required, bounded, and may contain only letters, digits, dot, dash, or underscore.");
        if (string.IsNullOrWhiteSpace(trigger.Name) || trigger.Name.Trim().Length > MaximumNameLength) errors.Add("Trigger name is required and bounded.");
        if (string.IsNullOrWhiteSpace(trigger.CaptureProfileId) || trigger.CaptureProfileId.Trim().Length > MaximumIdentifierLength) errors.Add("Capture profile id is required and bounded.");
        if (string.IsNullOrWhiteSpace(trigger.WorkflowId) || trigger.WorkflowId.Trim().Length > MaximumIdentifierLength) errors.Add("Workflow id is required and bounded.");
        if (trigger.CooldownSeconds is < MinimumCooldownSeconds or > MaximumCooldownSeconds) errors.Add($"Cooldown must be between {MinimumCooldownSeconds} and {MaximumCooldownSeconds} seconds.");

        switch (trigger.Kind)
        {
            case WorkflowTriggerKind.Schedule:
                if (trigger.Schedule is null || !TimeOnly.TryParseExact(trigger.Schedule.TimeOfDay, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || trigger.Schedule.Days == WorkflowTriggerDays.None || (trigger.Schedule.Days & ~WorkflowTriggerDays.EveryDay) != 0)
                    errors.Add("Schedule requires HH:mm local time and at least one valid day.");
                break;
            case WorkflowTriggerKind.FileChange:
                if (trigger.FileChange is null || !IsSafeLocalFolder(trigger.FileChange.FolderPath)) errors.Add("File trigger requires a fully-qualified local folder path.");
                if (trigger.FileChange is { Filter.Length: > MaximumFilterLength } || trigger.FileChange?.Filter.Contains('\\') == true || trigger.FileChange?.Filter.Contains('/') == true)
                    errors.Add("File trigger filter is invalid or too long.");
                break;
            case WorkflowTriggerKind.ClipboardChange:
                break;
            case WorkflowTriggerKind.ForegroundWindow:
                if (string.IsNullOrWhiteSpace(trigger.Window?.Pattern) || trigger.Window.Pattern.Trim().Length > MaximumPatternLength) errors.Add("Window trigger requires a bounded title/process pattern.");
                break;
            case WorkflowTriggerKind.ProcessStart:
                if (!IsSafeProcessName(trigger.Process?.ProcessName)) errors.Add("Process trigger requires a process name without a path.");
                break;
            case WorkflowTriggerKind.Hotkey:
                if (!IsValidHotkey(trigger.Hotkey)) errors.Add("Hotkey trigger requires Ctrl/Alt/Shift/Win plus a valid key.");
                break;
            default:
                errors.Add("Unknown trigger kind.");
                break;
        }
        return new(errors.Count == 0, errors);
    }

    public static bool IsCaptureProfileUnattendedSafe(CaptureProfile? profile) => profile is not null && profile.Source switch
    {
        CaptureProfileSource.Region => profile.Region is { } region && !region.IsEmpty,
        CaptureProfileSource.ForegroundWindow => true,
        CaptureProfileSource.ActiveMonitor => true,
        CaptureProfileSource.VirtualDesktop => true,
        CaptureProfileSource.Scrolling => false,
        _ => false
    };

    public static bool IsValidHotkey(HotkeyGesture? gesture)
    {
        if (gesture is not { Modifiers: not HotkeyModifiers.None } value) return false;
        var key = value.VirtualKey;
        return key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87;
    }

    public static bool IsSafeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        return value.Length <= MaximumIdentifierLength && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsSafeLocalFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024) return false;
        var trimmed = path.Trim();
        if (trimmed.Length < 3 || !char.IsLetter(trimmed[0]) || trimmed[1] != ':' || (trimmed[2] != '\\' && trimmed[2] != '/')) return false;
        return !trimmed.Contains('\0');
    }

    private static bool IsSafeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var value = processName.Trim();
        if (value.Length > MaximumIdentifierLength || value.IndexOfAny(['\\', '/', ':']) >= 0) return false;
        return value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}
