using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Workflows;

public enum WorkflowTriggerKind
{
    Schedule,
    FileChange,
    ClipboardChange,
    ForegroundWindow,
    ProcessStart,
    Hotkey
}

[Flags]
public enum WorkflowTriggerDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    EveryDay = Weekdays | Weekend
}

public sealed record WorkflowTriggerSchedule(string TimeOfDay, WorkflowTriggerDays Days = WorkflowTriggerDays.EveryDay);
public sealed record WorkflowTriggerFileChange(string FolderPath, string Filter = "*.*", bool IncludeSubdirectories = false);
public sealed record WorkflowTriggerWindow(string Pattern);
public sealed record WorkflowTriggerProcess(string ProcessName);

public sealed record WorkflowTrigger(
    string Id,
    string Name,
    WorkflowTriggerKind Kind,
    string CaptureProfileId,
    string WorkflowId,
    bool Enabled = true,
    int CooldownSeconds = 5,
    WorkflowTriggerSchedule? Schedule = null,
    WorkflowTriggerFileChange? FileChange = null,
    WorkflowTriggerWindow? Window = null,
    WorkflowTriggerProcess? Process = null,
    HotkeyGesture? Hotkey = null);

public enum WorkflowTriggerRunStatus
{
    Succeeded,
    Failed,
    Suppressed
}

public sealed record WorkflowTriggerHistoryEntry(
    string Id,
    string TriggerId,
    string TriggerName,
    WorkflowTriggerKind Kind,
    WorkflowTriggerRunStatus Status,
    string ReasonCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc);

public sealed record WorkflowTriggerValidationResult(bool IsValid, IReadOnlyList<string> Errors);
