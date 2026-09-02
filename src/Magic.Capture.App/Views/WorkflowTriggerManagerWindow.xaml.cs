using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Workflows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Magic.Capture.App.Views;

public sealed partial class WorkflowTriggerManagerWindow : Window
{
    private readonly ApplicationServices _services;
    private IReadOnlyList<WorkflowTrigger> _triggers = [];
    private IReadOnlyList<CaptureWorkflow> _workflows = [];
    private string? _editingId;

    internal WorkflowTriggerManagerWindow(ApplicationServices services)
    {
        InitializeComponent();
        _services = services;
        TriggerKindCombo.ItemsSource = Enum.GetValues<WorkflowTriggerKind>();
        TriggerKindCombo.SelectedIndex = 0;
        TriggerEnabledCheck.IsChecked = true;
        Closed += (_, _) => { };
        Platform.WindowHelpers.MoveAndResize(this, 110, 70, 1040, 780);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _workflows = await _services.Workflows.LoadAsync();
            TriggerWorkflowCombo.ItemsSource = _workflows;
            TriggerProfileCombo.ItemsSource = _services.Settings.CaptureProfiles;
            await RefreshTriggersAsync();
            await RefreshHistoryAsync();
        }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            ShowMessage("Trigger manager could not initialize. " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task RefreshTriggersAsync(string? selectId = null)
    {
        _triggers = await _services.WorkflowTriggers.LoadAsync();
        TriggerList.ItemsSource = null;
        TriggerList.ItemsSource = _triggers;
        var target = selectId ?? _editingId;
        TriggerList.SelectedItem = target is null ? null : _triggers.FirstOrDefault(trigger => string.Equals(trigger.Id, target, StringComparison.Ordinal));
        RefreshHotkeyStatus();
    }

    private async Task RefreshHistoryAsync()
    {
        var entries = await _services.WorkflowTriggerHistory.LoadAsync();
        TriggerHistoryList.ItemsSource = entries.Select(entry =>
            $"{entry.StartedUtc.LocalDateTime:g} · {entry.TriggerName} · {entry.Kind} · {entry.Status} · {entry.ReasonCode}").ToArray();
    }

    private void NewTrigger_Click(object sender, RoutedEventArgs e) => ResetEditor();
    private async void RefreshTriggers_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshTriggersAsync(); }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { ShowMessage(ex.Message, InfoBarSeverity.Error); }
    }

    private void TriggerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TriggerList.SelectedItem is not WorkflowTrigger trigger) return;
        LoadEditor(trigger);
    }

    private void TriggerKindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateKindPanels();

    private async void SaveTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (!_services.Entitlements.CanUse(ProductFeature.AdvancedWorkflows))
        {
            ShowMessage("Workflow automation triggers require Plus trial or Pro.", InfoBarSeverity.Warning);
            return;
        }
        try
        {
            var trigger = BuildTriggerFromEditor();
            var validation = WorkflowTriggerPolicy.Validate(trigger);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            var profile = _services.Settings.CaptureProfiles.FirstOrDefault(item => string.Equals(item.Id, trigger.CaptureProfileId, StringComparison.Ordinal));
            if (!WorkflowTriggerPolicy.IsCaptureProfileUnattendedSafe(profile))
                throw new InvalidDataException("Choose a capture profile that can run without interactive selection. Exact Region, Foreground Window, Active Monitor, and Virtual Desktop are supported.");
            if (!_workflows.Any(item => string.Equals(item.Id, trigger.WorkflowId, StringComparison.Ordinal)))
                throw new InvalidDataException("Choose an existing workflow.");

            await PersistTriggerSafelyAsync(trigger);
            await _services.WorkflowTriggerEngine.ReloadAsync();
            _editingId = trigger.Id;
            await RefreshTriggersAsync(trigger.Id);
            ShowMessage("Trigger saved.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("WorkflowTriggerSave", ex);
            ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void TestTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (TriggerList.SelectedItem is not WorkflowTrigger trigger)
        {
            ShowMessage("Save and select a trigger before testing.", InfoBarSeverity.Warning);
            return;
        }
        try
        {
            await _services.WorkflowTriggerRunner.RunAsync(trigger.Id, trigger.Kind, "test");
            await RefreshHistoryAsync();
            ShowMessage("Test request finished. See trigger history for the result.", InfoBarSeverity.Informational);
        }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("WorkflowTriggerTest", ex);
            ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void DeleteTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (TriggerList.SelectedItem is not WorkflowTrigger trigger) return;
        try
        {
            await _services.WorkflowTriggers.DeleteAsync(trigger.Id);
            try { await _services.WorkflowTaskScheduler.DeleteAsync(trigger.Id); }
            catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { _services.Log.Error("WorkflowTriggerTaskDelete", ex); }
            await _services.WorkflowTriggerEngine.ReloadAsync();
            ResetEditor();
            await RefreshTriggersAsync();
            ShowMessage("Trigger deleted. A stale scheduled task, if Windows refused deletion, cannot execute because the trigger id no longer exists.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("WorkflowTriggerDelete", ex);
            ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshHistoryAsync(); }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { ShowMessage(ex.Message, InfoBarSeverity.Error); }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        try { await _services.WorkflowTriggerHistory.ClearAsync(); await RefreshHistoryAsync(); }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { ShowMessage(ex.Message, InfoBarSeverity.Error); }
    }

    private async Task PersistTriggerSafelyAsync(WorkflowTrigger trigger)
    {
        if (trigger.Kind == WorkflowTriggerKind.Schedule && trigger.Enabled)
        {
            // Persist a fail-safe disabled record first. If the process exits or Task Scheduler
            // registration fails before the final commit, a stale/partial OS task can only reach
            // a disabled trigger and therefore cannot run the workflow.
            await _services.WorkflowTriggers.SaveAsync(trigger with { Enabled = false });
            try
            {
                await _services.WorkflowTaskScheduler.CreateOrUpdateAsync(trigger);
            }
            catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
            {
                try { await _services.WorkflowTaskScheduler.DeleteAsync(trigger.Id); }
                catch (Exception cleanupEx) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(cleanupEx))
                {
                    _services.Log.Error("WorkflowTriggerTaskCleanup", cleanupEx);
                }
                throw;
            }

            try
            {
                await _services.WorkflowTriggers.SaveAsync(trigger);
            }
            catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
            {
                // The atomic store still contains the disabled record from above. Remove the OS
                // task best-effort; even if Windows refuses, --trigger will observe Disabled.
                try { await _services.WorkflowTaskScheduler.DeleteAsync(trigger.Id); }
                catch (Exception cleanupEx) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(cleanupEx))
                {
                    _services.Log.Error("WorkflowTriggerTaskCleanup", cleanupEx);
                }
                throw;
            }
            return;
        }

        // Commit the local authority first. A stale schedule task is harmless after a disable or
        // kind change because the runner checks Enabled and expectedKind=Schedule on CLI dispatch.
        await _services.WorkflowTriggers.SaveAsync(trigger);
        try { await _services.WorkflowTaskScheduler.DeleteAsync(trigger.Id); }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("WorkflowTriggerTaskDelete", ex);
        }
    }

    private WorkflowTrigger BuildTriggerFromEditor()
    {
        var id = _editingId ?? Guid.NewGuid().ToString("N");
        if (TriggerKindCombo.SelectedItem is not WorkflowTriggerKind kind) throw new InvalidDataException("Choose a trigger type.");
        var profile = TriggerProfileCombo.SelectedItem as Magic.Capture.Core.Capture.CaptureProfile ?? throw new InvalidDataException("Choose a capture profile.");
        var workflow = TriggerWorkflowCombo.SelectedItem as CaptureWorkflow ?? throw new InvalidDataException("Choose a workflow.");
        var cooldown = double.IsFinite(TriggerCooldownBox.Value) ? (int)Math.Round(TriggerCooldownBox.Value) : 5;
        return new WorkflowTrigger(
            id,
            TriggerNameBox.Text.Trim(),
            kind,
            profile.Id,
            workflow.Id,
            TriggerEnabledCheck.IsChecked == true,
            cooldown,
            Schedule: kind == WorkflowTriggerKind.Schedule ? new WorkflowTriggerSchedule(ScheduleTimeBox.Text.Trim(), ParseDays(ScheduleDaysBox.Text)) : null,
            FileChange: kind == WorkflowTriggerKind.FileChange ? new WorkflowTriggerFileChange(FileFolderBox.Text.Trim(), string.IsNullOrWhiteSpace(FileFilterBox.Text) ? "*.*" : FileFilterBox.Text.Trim(), FileRecursiveCheck.IsChecked == true) : null,
            Window: kind == WorkflowTriggerKind.ForegroundWindow ? new WorkflowTriggerWindow(WindowPatternBox.Text.Trim()) : null,
            Process: kind == WorkflowTriggerKind.ProcessStart ? new WorkflowTriggerProcess(ProcessNameBox.Text.Trim()) : null,
            Hotkey: kind == WorkflowTriggerKind.Hotkey ? ParseHotkey(HotkeyBox.Text) : null);
    }

    private void LoadEditor(WorkflowTrigger trigger)
    {
        _editingId = trigger.Id;
        TriggerNameBox.Text = trigger.Name;
        TriggerKindCombo.SelectedItem = trigger.Kind;
        TriggerEnabledCheck.IsChecked = trigger.Enabled;
        TriggerCooldownBox.Value = trigger.CooldownSeconds;
        TriggerProfileCombo.SelectedItem = _services.Settings.CaptureProfiles.FirstOrDefault(item => string.Equals(item.Id, trigger.CaptureProfileId, StringComparison.Ordinal));
        TriggerWorkflowCombo.SelectedItem = _workflows.FirstOrDefault(item => string.Equals(item.Id, trigger.WorkflowId, StringComparison.Ordinal));
        ScheduleTimeBox.Text = trigger.Schedule?.TimeOfDay ?? "09:00";
        ScheduleDaysBox.Text = FormatDays(trigger.Schedule?.Days ?? WorkflowTriggerDays.Weekdays);
        FileFolderBox.Text = trigger.FileChange?.FolderPath ?? string.Empty;
        FileFilterBox.Text = trigger.FileChange?.Filter ?? "*.*";
        FileRecursiveCheck.IsChecked = trigger.FileChange?.IncludeSubdirectories == true;
        WindowPatternBox.Text = trigger.Window?.Pattern ?? string.Empty;
        ProcessNameBox.Text = trigger.Process?.ProcessName ?? string.Empty;
        HotkeyBox.Text = trigger.Hotkey is null ? string.Empty : FormatHotkey(trigger.Hotkey);
        UpdateKindPanels();
        RefreshHotkeyStatus();
    }

    private void ResetEditor()
    {
        _editingId = null;
        TriggerList.SelectedItem = null;
        TriggerNameBox.Text = "New workflow trigger";
        TriggerKindCombo.SelectedItem = WorkflowTriggerKind.Schedule;
        TriggerEnabledCheck.IsChecked = true;
        TriggerCooldownBox.Value = 5;
        TriggerProfileCombo.SelectedIndex = _services.Settings.CaptureProfiles.Count > 0 ? 0 : -1;
        TriggerWorkflowCombo.SelectedIndex = _workflows.Count > 0 ? 0 : -1;
        ScheduleTimeBox.Text = "09:00";
        ScheduleDaysBox.Text = "MON,TUE,WED,THU,FRI";
        FileFolderBox.Text = string.Empty;
        FileFilterBox.Text = "*.*";
        FileRecursiveCheck.IsChecked = false;
        WindowPatternBox.Text = string.Empty;
        ProcessNameBox.Text = string.Empty;
        HotkeyBox.Text = "Ctrl+Shift+K";
        UpdateKindPanels();
    }

    private void UpdateKindPanels()
    {
        var kind = TriggerKindCombo.SelectedItem is WorkflowTriggerKind selected ? selected : WorkflowTriggerKind.Schedule;
        SchedulePanel.Visibility = kind == WorkflowTriggerKind.Schedule ? Visibility.Visible : Visibility.Collapsed;
        FilePanel.Visibility = kind == WorkflowTriggerKind.FileChange ? Visibility.Visible : Visibility.Collapsed;
        ClipboardPanel.Visibility = kind == WorkflowTriggerKind.ClipboardChange ? Visibility.Visible : Visibility.Collapsed;
        WindowPanel.Visibility = kind == WorkflowTriggerKind.ForegroundWindow ? Visibility.Visible : Visibility.Collapsed;
        ProcessPanel.Visibility = kind == WorkflowTriggerKind.ProcessStart ? Visibility.Visible : Visibility.Collapsed;
        HotkeyPanel.Visibility = kind == WorkflowTriggerKind.Hotkey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHotkeyStatus()
    {
        var errors = _services.WorkflowTriggerEngine.HotkeyRegistrationErrors;
        HotkeyRegistrationStatusText.Text = errors.Count == 0
            ? "Resident trigger sources are bounded and entitlement-aware."
            : "Hotkey registration issues: " + string.Join(" · ", errors.Select(item => $"{item.Key}: {item.Value}"));
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        TriggerInfoBar.Message = message;
        TriggerInfoBar.Severity = severity;
        TriggerInfoBar.IsOpen = true;
    }

    private static WorkflowTriggerDays ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return WorkflowTriggerDays.None;
        var result = WorkflowTriggerDays.None;
        foreach (var token in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToUpperInvariant())
            {
                case "ALL": return WorkflowTriggerDays.EveryDay;
                case "WEEKDAYS": result |= WorkflowTriggerDays.Weekdays; break;
                case "WEEKEND": result |= WorkflowTriggerDays.Weekend; break;
                case "MON": result |= WorkflowTriggerDays.Monday; break;
                case "TUE": result |= WorkflowTriggerDays.Tuesday; break;
                case "WED": result |= WorkflowTriggerDays.Wednesday; break;
                case "THU": result |= WorkflowTriggerDays.Thursday; break;
                case "FRI": result |= WorkflowTriggerDays.Friday; break;
                case "SAT": result |= WorkflowTriggerDays.Saturday; break;
                case "SUN": result |= WorkflowTriggerDays.Sunday; break;
                default: throw new InvalidDataException($"Unknown schedule day: {token}");
            }
        }
        return result;
    }

    private static string FormatDays(WorkflowTriggerDays days)
    {
        if (days == WorkflowTriggerDays.EveryDay) return "ALL";
        if (days == WorkflowTriggerDays.Weekdays) return "WEEKDAYS";
        var values = new List<string>();
        if (days.HasFlag(WorkflowTriggerDays.Monday)) values.Add("MON");
        if (days.HasFlag(WorkflowTriggerDays.Tuesday)) values.Add("TUE");
        if (days.HasFlag(WorkflowTriggerDays.Wednesday)) values.Add("WED");
        if (days.HasFlag(WorkflowTriggerDays.Thursday)) values.Add("THU");
        if (days.HasFlag(WorkflowTriggerDays.Friday)) values.Add("FRI");
        if (days.HasFlag(WorkflowTriggerDays.Saturday)) values.Add("SAT");
        if (days.HasFlag(WorkflowTriggerDays.Sunday)) values.Add("SUN");
        return string.Join(',', values);
    }

    private static HotkeyGesture ParseHotkey(string? text)
    {
        var tokens = (text ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2) throw new InvalidDataException("Hotkey must include a modifier and a key, for example Ctrl+Shift+K.");
        var modifiers = HotkeyModifiers.None;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            modifiers |= tokens[index].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => throw new InvalidDataException($"Unknown hotkey modifier: {tokens[index]}")
            };
        }
        var key = tokens[^1].Trim().ToUpperInvariant();
        int virtualKey;
        if (key.Length == 1 && char.IsLetterOrDigit(key[0])) virtualKey = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24) virtualKey = 0x70 + functionKey - 1;
        else throw new InvalidDataException("Hotkey key must be A-Z, 0-9, or F1-F24.");
        var gesture = new HotkeyGesture(modifiers, virtualKey);
        if (!WorkflowTriggerPolicy.IsValidHotkey(gesture)) throw new InvalidDataException("Hotkey is not valid.");
        return gesture;
    }

    private static string FormatHotkey(HotkeyGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        if (gesture.VirtualKey is >= 0x70 and <= 0x87) parts.Add("F" + (gesture.VirtualKey - 0x70 + 1));
        else parts.Add(((char)gesture.VirtualKey).ToString().ToUpperInvariant());
        return string.Join('+', parts);
    }
}
