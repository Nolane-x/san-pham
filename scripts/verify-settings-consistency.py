#!/usr/bin/env python3
from pathlib import Path
import re, sys
root = Path(__file__).resolve().parents[1]
errors=[]

def need(path, needle, msg):
    target=root/path
    if not target.exists():
        errors.append(msg + ' (file missing)')
        return
    text=target.read_text(encoding='utf-8')
    if needle not in text: errors.append(msg)

def forbid(path, needle, msg):
    target=root/path
    if not target.exists(): return
    text=target.read_text(encoding='utf-8')
    if needle in text: errors.append(msg)

need(Path('src/Magic.Capture.App/ApplicationServices.cs'),'CommitSettingsSnapshot','ApplicationServices must expose controlled settings commit')
appsvc=(root/'src/Magic.Capture.App/ApplicationServices.cs').read_text(encoding='utf-8')
if re.search(r'public\s+AppSettings\s+Settings\s*\{[^}]*\bset\s*=>', appsvc, re.S): errors.append('ApplicationServices.Settings must not have a public setter')
need(Path('src/Magic.Capture.App/App.xaml.cs'),'_settingsMutationGate','App must serialize settings mutations')
need(Path('src/Magic.Capture.App/App.xaml.cs'),'MutateSettingsAsync','App must expose functional settings mutation authority')
need(Path('src/Magic.Capture.App/App.xaml.cs'),'ReloadSettingsFromStoreUnsafeAsync','configuration import must reconcile through strict settings reload')
need(Path('src/Magic.Capture.App/Persistence/SettingsStore.cs'),'LoadStrictAsync','SettingsStore must distinguish strict reload from fail-soft startup read')
need(Path('src/Magic.Capture.App/Persistence/SettingsStore.cs'),'?? throw new InvalidDataException("Settings file is missing.")','strict settings reload must reject a missing settings file instead of normalizing null/default state')
need(Path('src/Magic.Capture.App/Platform/HotkeyService.cs'),'TryApplyConfiguration','HotkeyService must apply region/repeat/personal hotkeys transactionally')
need(Path('src/Magic.Capture.App/Platform/HotkeyService.cs'),'LastRollbackSucceeded','HotkeyService must report rollback failure')

for legacy in ('public void UnregisterRegionCapture()', 'public void UnregisterRepeatCapture()', 'public void UnregisterPersonalHotkeys()'):
    forbid(Path('src/Magic.Capture.App/Platform/HotkeyService.cs'), legacy, 'HotkeyService must not expose native hotkey unregister bypasses outside its transaction API')
need(Path('src/Magic.Capture.Core/Settings/SettingsReferencePolicy.cs'),'RemoveWorkflowReferences','Core must clean workflow settings references')
need(Path('src/Magic.Capture.Core/Settings/SettingsReferencePolicy.cs'),'RemoveMagicActionReferences','Core must clean Magic Action settings references')
need(Path('src/Magic.Capture.Core/Settings/SettingsReferencePolicy.cs'),'RemoveCaptureProfileReferences','Core must clean capture-profile settings references')
need(Path('src/Magic.Capture.Core/Settings/SettingsReferencePolicy.cs'),'RequiresExternalReferencePrune','Core must detect stale external settings references without forcing a startup write')
need(Path('src/Magic.Capture.App/App.xaml.cs'),'ReconcileSettingsReferencesAtStartupAsync','startup must self-heal settings references left stale by cross-resource failures')
need(Path('src/Magic.Capture.Core/Workflows/WorkflowReferencePolicy.cs'),'FindWorkflowDependents','Core must block deletion of referenced workflows')
need(Path('src/Magic.Capture.Core/Workflows/WorkflowReferencePolicy.cs'),'FindCaptureProfileDependents','Core must block deletion of referenced capture profiles')
need(Path('src/Magic.Capture.Core/Workflows/WorkflowReferencePolicy.cs'),'FindMagicActionDependents','Core must block deletion of referenced Magic Actions')
need(Path('src/Magic.Capture.Core/Workflows/WorkflowReferencePolicy.cs'),'FindLocalActionDependents','Core must block deletion of referenced Local Actions')
need(Path('src/Magic.Capture.Core/Workflows/WorkflowReferencePolicy.cs'),'FindDestinationDependents','Core must block deletion of referenced destinations')
need(Path('src/Magic.Capture.App/MainWindow.xaml.cs'),'WorkflowReferencePolicy.FindLocalActionDependents','Local Action deletion must consult dependency policy before commit')
need(Path('src/Magic.Capture.App/MainWindow.xaml.cs'),'WorkflowReferencePolicy.FindDestinationDependents','destination deletion must consult dependency policy before commit')
need(Path('src/Magic.Capture.App/Workflows/WorkflowTriggerStore.cs'),'DisableDanglingAsync','trigger store must disable dangling imported trigger references in one transaction')

for rel in [
    'src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs',
    'src/Magic.Capture.App/Views/PinWindow.xaml.cs',
    'src/Magic.Capture.App/Views/DesignToolsWindow.xaml.cs',
    'src/Magic.Capture.App/MainWindow.xaml.cs']:
    text=(root/rel).read_text(encoding='utf-8')
    if 'SettingsStore.TrySaveAsync' in text or 'SettingsStore.SaveAsync' in text:
        errors.append(f'{rel} must not write settings storage directly')
    if re.search(r'\b_services\.Settings\s*=|\bServices\.Settings\s*=', text):
        errors.append(f'{rel} must not assign ApplicationServices.Settings directly')

for rel in ['src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs','src/Magic.Capture.App/Views/PinWindow.xaml.cs','src/Magic.Capture.App/Views/DesignToolsWindow.xaml.cs']:
    need(Path(rel),'TryMutateSettingsAsync',f'{rel} must route settings mutation through App authority')

forbid(Path('src/Magic.Capture.App/MainWindow.xaml.cs'),'TryPersistSettingsSnapshotAsync','MainWindow must not keep whole-snapshot settings persistence helper')
forbid(Path('src/Magic.Capture.App/App.xaml.cs'),'internal async Task UpdateSettingsAsync(AppSettings settings','App must not expose whole-snapshot UpdateSettingsAsync')

app_text=(root/'src/Magic.Capture.App/App.xaml.cs').read_text(encoding='utf-8')
startup_reconcile=app_text.find('await ReconcileSettingsReferencesAtStartupAsync')
startup_hotkeys=app_text.find('TryApplyHotkeysForSettings(Services.Settings)', startup_reconcile if startup_reconcile >= 0 else 0)
if startup_reconcile < 0 or startup_hotkeys < 0 or startup_reconcile > startup_hotkeys:
    errors.append('startup external-reference reconciliation must occur before initial native hotkey registration')
need(Path('.github/workflows/windows-ci.yml'),'-p:Platform=${{ matrix.platform }}','Windows CI must restore/build per platform') if (root/'.github/workflows/windows-ci.yml').exists() else errors.append('Windows CI workflow is required')

print('Magic Capture Desktop settings consistency verifier')
print(f'  Errors: {len(errors)}')
for e in errors: print('  - '+e)
sys.exit(1 if errors else 0)
