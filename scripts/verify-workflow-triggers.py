#!/usr/bin/env python3
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[1]
checks = {
    'src/Magic.Capture.Core/Workflows/WorkflowTriggerModels.cs': [
        'enum WorkflowTriggerKind', 'Schedule', 'FileChange', 'ClipboardChange', 'ForegroundWindow', 'ProcessStart', 'Hotkey',
        'record WorkflowTrigger(', 'CaptureProfileId', 'WorkflowId', 'CooldownSeconds', 'WorkflowTriggerSchedule', 'WorkflowTriggerFileChange',
        'WorkflowTriggerWindow', 'WorkflowTriggerProcess', 'HotkeyGesture'
    ],
    'src/Magic.Capture.Core/Workflows/WorkflowTriggerPolicy.cs': [
        'MaximumTriggers = 64', 'MaximumHotkeyTriggers = 16', 'MinimumCooldownSeconds = 1', 'MaximumCooldownSeconds = 3600',
        'CircuitBreakerMaximumRuns = 20', 'CircuitBreakerWindow = TimeSpan.FromMinutes(5)', 'CircuitBreakerSuspension = TimeSpan.FromMinutes(10)',
        'ValidateSet', 'IsCaptureProfileUnattendedSafe', 'CultureInfo.InvariantCulture', 'DateTimeStyles.None',
        'public static bool IsSafeIdentifier', "trimmed[1] != ':'", "trimmed[2] != '\\\\'", "Filter.Contains('\\\\')", "IndexOfAny(['\\\\', '/', ':'])"
    ],
    'src/Magic.Capture.Core/Cli/CliCommand.cs': ['TriggerCliCommand'],
    'src/Magic.Capture.Core/Cli/CliParser.cs': ['--trigger', 'TriggerCliCommand', 'WorkflowTriggerPolicy.IsSafeIdentifier(id)'],
    'src/Magic.Capture.App/Persistence/AppPaths.cs': ['WorkflowTriggersFile', 'WorkflowTriggerHistoryFile'],
    'src/Magic.Capture.App/Workflows/WorkflowTriggerStore.cs': ['WorkflowTriggerStore', 'SaveAsync', 'DeleteAsync', 'Changed'],
    'src/Magic.Capture.App/Workflows/WorkflowTriggerHistoryStore.cs': ['WorkflowTriggerHistoryStore', 'MaximumEntries = 200', 'reasonCode'],
    'src/Magic.Capture.App/Workflows/WorkflowTriggerRunner.cs': [
        'WorkflowTriggerRunner', 'AdvancedWorkflows', 'SemaphoreSlim', 'trigger_kind_mismatch', 'CircuitBreakerMaximumRuns',
        '_runCaptureProfileForAutomationAsync', 'LastCompletedUtc', 'RecordBestEffortAsync'
    ],
    'src/Magic.Capture.App/Workflows/ResidentWorkflowTriggerEngine.cs': [
        'ResidentWorkflowTriggerEngine', 'FileSystemWatcher', 'AddClipboardFormatListener', 'SetWinEventHook', 'PeriodicTimer',
        'WorkflowTriggerHotkeyService', 'AdvancedWorkflows', 'ReloadAsync', 'StopAsync', '_pendingTriggerIds', 'WinEventSkipOwnProcess'
    ],
    'src/Magic.Capture.App/Workflows/WorkflowTriggerHotkeyService.cs': ['WorkflowTriggerHotkeyService', 'WorkflowTriggerHotkeyEventArgs', 'RegisterHotKey', 'UnregisterHotKey'],
    'src/Magic.Capture.App/Workflows/WindowsTaskSchedulerService.cs': ['WindowsTaskSchedulerService', 'schtasks.exe', 'ArgumentList', '/IT', 'LIMITED', '--trigger', 'WorkflowTriggerPolicy.IsSafeIdentifier(triggerId)', 'TaskPrefix = "Magic Capture Desktop - Workflow - "'],
    'src/Magic.Capture.App/App.xaml.cs': ['case TriggerCliCommand', 'WorkflowTriggerRunner', 'ResidentWorkflowTriggerEngine', 'RunCaptureProfileForAutomationAsync', 'WorkflowTriggerEngine.ReloadAsync', '_launchCliArgs.Length == 0 && Services.Entitlements.ShouldShowTrialExpiredNotice', 'case CaptureProfileSource.Region:\n                    if (automation)', 'StoreWorkflowFailureTraceBestEffortAsync', 'FatalExceptionPolicy.IsFatal'],
    'src/Magic.Capture.App/ApplicationServices.cs': ['WorkflowTriggers', 'WorkflowTriggerHistory', 'WorkflowTriggerRunner', 'WorkflowTriggerEngine', 'WorkflowTaskScheduler'],
    'src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml': ['Workflow triggers', 'Save', 'Test now', 'Recent trigger history'],
    'src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml.cs': ['WorkflowTriggerManagerWindow', 'SaveTrigger_Click', 'TestTrigger_Click', 'DeleteTrigger_Click', 'PersistTriggerSafelyAsync', 'trigger with { Enabled = false }'],
    'src/Magic.Capture.App/MainWindow.xaml': ['WorkflowTriggerManager_Click'],
    'src/Magic.Capture.App/MainWindow.xaml.cs': ['WorkflowTriggerManager_Click'],
}
errors=[]
ui_path = ROOT / 'src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml.cs'
if ui_path.exists():
    ui_text = ui_path.read_text(encoding='utf-8', errors='replace')
    safe_delete_order = 'await _services.WorkflowTriggers.DeleteAsync(trigger.Id);\n            try { await _services.WorkflowTaskScheduler.DeleteAsync(trigger.Id); }'
    if safe_delete_order not in ui_text:
        errors.append('WorkflowTriggerManagerWindow.xaml.cs: trigger config must be deleted before best-effort scheduler cleanup')
    if 'try { await _services.WorkflowTaskScheduler.CreateOrUpdateAsync(trigger); }\n                catch\n                {' in ui_text:
        errors.append('WorkflowTriggerManagerWindow.xaml.cs: scheduler save must not use a bare catch')
for rel, needles in checks.items():
    p=ROOT/rel
    if not p.exists():
        errors.append(f'missing file: {rel}')
        continue
    text=p.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in text:
            errors.append(f'{rel}: missing {needle!r}')


# Privacy / isolation negatives: trigger matching must not become a second content store or network worker.
history_path = ROOT / 'src/Magic.Capture.App/Workflows/WorkflowTriggerHistoryStore.cs'
if history_path.exists():
    history_text = history_path.read_text(encoding='utf-8', errors='replace')
    for forbidden in ('PngBytes', 'OcrText', 'ClipboardText', 'WindowTitle', 'CommandLine', 'ResponseBody', 'Stdout', 'Stderr'):
        if forbidden in history_text: errors.append(f'trigger history privacy contract forbids payload field {forbidden}')
resident_path = ROOT / 'src/Magic.Capture.App/Workflows/ResidentWorkflowTriggerEngine.cs'
if resident_path.exists():
    resident_text = resident_path.read_text(encoding='utf-8', errors='replace')
    for forbidden in ('HttpClient', 'WebRequest', 'GetClipboardData', 'Clipboard.Get'):
        if forbidden in resident_text: errors.append(f'resident trigger engine must not use {forbidden}')
scheduler_path = ROOT / 'src/Magic.Capture.App/Workflows/WindowsTaskSchedulerService.cs'
if scheduler_path.exists():
    scheduler_text = scheduler_path.read_text(encoding='utf-8', errors='replace').lower()
    for forbidden in ('cmd.exe', 'powershell.exe', 'pwsh.exe'):
        if forbidden in scheduler_text: errors.append(f'task scheduler integration must not invoke shell {forbidden}')
policy_path = ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowTriggerPolicy.cs'
if policy_path.exists() and 'Path.IsPathFullyQualified' in policy_path.read_text(encoding='utf-8', errors='replace'):
    errors.append('trigger local-folder validation must not depend on host-OS Path.IsPathFullyQualified semantics')

# 4.12 release-truth compatibility. On the historical 4.12 tree, pin exact version/counts.
# On later releases, require the seven trigger features to remain Done and retain historical 4.12 documentation.
try:
    import json
    version = json.loads((ROOT / 'release/version.json').read_text(encoding='utf-8'))
    audit = json.loads((ROOT / 'release/feature-audit-660.json').read_text(encoding='utf-8'))
    source_version = audit.get('sourceVersion')
    statuses = {int(item['id']): item['status'] for item in audit.get('features', [])}
    for feature_id in range(438, 445):
        if statuses.get(feature_id) != 'Done': errors.append(f'feature #{feature_id} must remain Done after 4.12')
    if audit.get('total') != 660 or len(audit.get('features', [])) != 660:
        errors.append('workflow-trigger audit must contain exactly 660 features')
    if source_version == '4.12.0':
        if version.get('semver') != '4.12.0': errors.append('4.12 tree semver must be 4.12.0')
        if version.get('msixVersion') != '4.12.0.0': errors.append('4.12 tree msixVersion must be 4.12.0.0')
        expected_counts = {'Done': 433, 'Partial': 61, 'Foundation': 108, 'Missing': 36, 'ReleaseTest': 22}
        if audit.get('counts') != expected_counts: errors.append(f'4.12 audit counts must equal {expected_counts}')
except Exception as exc:
    errors.append(f'workflow-trigger release metadata could not be read: {exc}')

release_files = {
    'README.md': ['4.12 Automation Triggers', '64 triggers', 'metadata records'],
    'docs/FEATURE_MATRIX.md': ['Automation Triggers — 4.12.0', 'ResidentWorkflowTriggerEngine.cs', 'WindowsTaskSchedulerService.cs'],
    'docs/WINDOWS_RELEASE_CHECKLIST.md': ['4.12 Automation Triggers', 'Task Scheduler', 'Clipboard trigger', 'process-start', 'entitlement'],
    'docs/RELEASE_NOTES_4.12.0.md': ['Automation Triggers', '433 Done', '#438', '#444', 'metadata-only'],
}
for rel, needles in release_files.items():
    path = ROOT / rel
    if not path.exists():
        errors.append(f'4.12 release file missing: {rel}')
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in text: errors.append(f'{rel}: missing historical 4.12 marker {needle!r}')

source_release_path = ROOT / 'scripts/source-release.py'
if source_release_path.exists() and 'verify-workflow-triggers.py' not in source_release_path.read_text(encoding='utf-8', errors='replace'):
    errors.append('source-release.py must run verify-workflow-triggers.py')

print('Magic Capture Desktop workflow trigger source contract')
print(f'  Errors: {len(errors)}')
for e in errors: print('ERROR:', e)
sys.exit(1 if errors else 0)
