from pathlib import Path
import json, sys
root=Path(__file__).resolve().parents[1]
errors=[]
def need(rel,*needles):
    p=root/rel
    if not p.exists():
        errors.append(f'missing file: {rel}'); return
    t=p.read_text(encoding='utf-8',errors='ignore')
    for n in needles:
        if n not in t: errors.append(f'{rel}: missing {n}')
need('src/Magic.Capture.Core/Settings/PersonalizationModels.cs',
     'enum PersonalHotkeyKind','record PersonalHotkeyBinding','record AnnotationStylePreset',
     'record MonitorCapturePreference','record AppCaptureRule','record PersonalizationActionItem','Display =>')
need('src/Magic.Capture.Core/Settings/AppSettings.cs',
     'PersonalHotkeys','ToolbarActions','OverlayActions','DefaultAnnotationTool','LastAnnotationTool',
     'RememberLastAnnotationTool','AnnotationStylePresets','MonitorPreferences','AppCaptureRules')
need('src/Magic.Capture.Core/Settings/AppSettingsRules.cs',
     'CurrentPersistenceSchemaVersion = 2','MaximumPersonalHotkeys = 48','MaximumAnnotationStylePresets = 24',
     'MaximumMonitorPreferences = 32','MaximumAppCaptureRules = 64','NormalizePersonalHotkeys',
     'NormalizeActionLayout','NormalizeAnnotationStyles','NormalizeMonitorPreferences','NormalizeAppCaptureRules','profile:',
     'ResetSection','SettingsSection.Hotkeys','SettingsSection.Personalization','SettingsSection.ContextPreferences')
need('src/Magic.Capture.App/Platform/HotkeyService.cs',
     'PersonalHotkeyRequested','TryApplyConfiguration','LastRollbackSucceeded','ActiveRegionHotkey','FirstPersonalHotkeyId')
need('src/Magic.Capture.App/App.xaml.cs',
     'DispatchPersonalHotkeyAsync','ResolveMonitorCapturePreferences','ResolveAppCaptureRule',
     'TryApplyHotkeysForSettings','PersonalHotkeyRequested','HotkeySettingsEquivalent','TryRunForegroundAppCaptureRuleAsync','StartsWith("profile:"')
need('src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs',
     'PreferredAnnotationTool','RememberLastAnnotationTool','PersistLastAnnotationToolAsync',
     'ApplyAnnotationStylePreset','SaveAnnotationStylePresetAsync')
need('src/Magic.Capture.App/MainWindow.xaml',
     'PersonalHotkeyList','ToolbarActionList','OverlayActionList','AnnotationStylePresetList',
     'MonitorPreferenceList','AppCaptureRuleList','ResetHotkeysSection_Click','ResetPersonalizationSection_Click',
     'ResetContextPreferencesSection_Click')
need('src/Magic.Capture.App/MainWindow.xaml.cs',
     'RefreshPersonalizationSettingsUi','SavePersonalHotkey_Click','MoveToolbarActionUp_Click',
     'MoveOverlayActionUp_Click','SaveMonitorPreference_Click','SaveAppCaptureRule_Click',
     'ResetSettingsSectionAsync','ValidatePersonalHotkeyTargetAsync','ResetCaptureSection_Click','ResetOutputSection_Click','ResetPrivacySection_Click','ResetHistorySection_Click')
need('tests/Magic.Capture.Core.Tests/PersonalizationSettingsTests.cs',
     'NormalizeForRuntime_BoundsPersonalizationCollections','NormalizeForRuntime_RemovesDuplicatePersonalHotkeys',
     'ResetSection_PreservesUnrelatedSettings')
need('scripts/source-release.py','verify-settings-personalization.py')

version=json.loads((root/'release/version.json').read_text(encoding='utf-8'))
def version_tuple(value):
    try: return tuple(int(part) for part in str(value).split('.')[:3])
    except ValueError: return (0,0,0)
if version_tuple(version.get('semver')) < (4,15,0):
    errors.append('source version predates required 4.15 settings personalization baseline')
else:
    audit=json.loads((root/'release/feature-audit-660.json').read_text(encoding='utf-8'))
    by={int(item['id']):item for item in audit.get('features',[])}
    for i in (588,589,590,591,596,597,598,599,600,601,602,603,604,605):
        if by.get(i,{}).get('status')!='Done': errors.append(f'feature #{i} regressed below Done after 4.15')
if version.get('semver')=='4.15.0':
    audit=json.loads((root/'release/feature-audit-660.json').read_text(encoding='utf-8'))
    expected={'Done':461,'Partial':46,'Foundation':95,'Missing':36,'ReleaseTest':22}
    if audit.get('counts')!=expected: errors.append(f'4.15 audit counts mismatch: {audit.get("counts")}')
    if audit.get('sourceVersion')!='4.15.0': errors.append('4.15 audit sourceVersion mismatch')
    if version.get('msixVersion')!='4.15.0.0': errors.append('4.15 msixVersion mismatch')
    need('src/Magic.Capture.App/Magic.Capture.App.csproj','<Version>4.15.0</Version>','<AssemblyVersion>4.15.0.0</AssemblyVersion>','<FileVersion>4.15.0.0</FileVersion>')
    need('src/Magic.Capture.App/Package.appxmanifest','Version="4.15.0.0"')
    need('README.md','# Magic Capture Desktop 4.15','Settings & Personalization Runtime')
    need('docs/FEATURE_MATRIX.md','# Magic Capture Desktop 4.15.0 — Feature Matrix','Settings & Personalization Runtime — 4.15.0')
    need('docs/WINDOWS_RELEASE_CHECKLIST.md','# Magic Capture Desktop 4.15.0 — Windows Release Checklist','461 `Done`, 46 `Partial`, 95 `Foundation`, 36 `Missing` and 22 `ReleaseTest`','Personal hotkey','Per-app capture rule')
    need('docs/RELEASE_NOTES_4.15.0.md','Settings & Personalization Runtime','461 Done / 46 Partial / 95 Foundation / 36 Missing / 22 ReleaseTest')

print('Magic Capture Desktop settings personalization verifier')
print('  Errors:',len(errors))
for e in errors: print('  ERROR:',e)
sys.exit(1 if errors else 0)
