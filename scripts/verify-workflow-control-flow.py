#!/usr/bin/env python3
from pathlib import Path
import json,re,sys

ROOT=Path(__file__).resolve().parents[1]
errors=[]

def text(rel):
    p=ROOT/rel
    if not p.exists():
        errors.append(f"missing file: {rel}")
        return ''
    return p.read_text(encoding='utf-8')

def need(rel,*needles):
    s=text(rel)
    for n in needles:
        if n not in s: errors.append(f"{rel}: missing {n}")
    return s

models=need('src/Magic.Capture.Core/Workflows/WorkflowModels.cs','ForEachImage')
policy=need('src/Magic.Capture.Core/Workflows/WorkflowRuntimePolicy.cs',
            'MaximumLoopImages = 32','RequiresSchemaV5','ParseLoopContinueOnError',
            'IsResumeSkippableSideEffect','IsResumeNonReplayable')
validator=need('src/Magic.Capture.Core/Workflows/WorkflowValidator.cs','ForEachImage','SchemaVersion is < 1 or > 5','ForEachImage cannot retry')
fingerprint=need('src/Magic.Capture.Core/Workflows/WorkflowFingerprint.cs','public static class WorkflowFingerprint','SHA256.HashData','Compute(CaptureWorkflow workflow)')
context=need('src/Magic.Capture.App/Workflows/WorkflowExecutionContext.cs','LoopAssets','IsResume','ResumeCompletedSideEffectStepIds')
executor=need('src/Magic.Capture.App/Workflows/WorkflowExecutor.cs','WorkflowLoopSummary','case WorkflowStepKind.ForEachImage','loop.index','loop.number','loop.count','ResumeCompletedSideEffectStepIds','IsResumeSkippableSideEffect','loopChildFailure')
if 'new Dictionary<string, string>(StringValues(values)' in executor: errors.append('WorkflowExecutor: loop variable map must not use IDictionary-only constructor with IReadOnlyDictionary')
trace=need('src/Magic.Capture.App/Workflows/WorkflowTraceStore.cs','Guid? AssetId','WorkflowFingerprint','Guid? ResumedFromTraceId','ResumeCompletedSideEffectStepIds','SchemaVersion is < 1 or > 5')
resume=need('src/Magic.Capture.App/Workflows/WorkflowResumePlanner.cs','WorkflowResumePlan','CreatePlan','IsResumeNonReplayable','WorkflowFingerprint.Compute','ResumeCompletedSideEffectStepIds','failedStep')
batch=need('src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs','asset.Id')
app=need('src/Magic.Capture.App/App.xaml.cs','loopAssets','resumedFromTraceId','assetId')
xaml=need('src/Magic.Capture.App/MainWindow.xaml','Run once with selected History as image loop','Resume selected failed trace')
main=need('src/Magic.Capture.App/MainWindow.xaml.cs','RunWorkflowLoopOnHistory_Click','ResumeWorkflowTrace_Click','LoadHistoryAssetByIdAsync','WorkflowResumePlanner.CreatePlan')
source_release=need('scripts/source-release.py','verify-workflow-control-flow.py')

# privacy: trace must not persist runtime values/payload fields
for forbidden in ['IReadOnlyDictionary<string, object?> Values','PngBytes','OcrText','HttpBody','Stdout','Stderr','PromptAnswer']:
    if forbidden in trace: errors.append(f'trace privacy: forbidden persisted token {forbidden}')

# 4.13 introduced loop/resume. Newer releases must preserve those invariants,
# while only the historical 4.13 source pins exact 4.13 metadata/counts below.
version=json.loads(text('release/version.json') or '{}')
def version_tuple(value):
    try: return tuple(int(part) for part in str(value).split('.')[:3])
    except ValueError: return (0, 0, 0)
if version_tuple(version.get('semver')) < (4, 13, 0):
    errors.append('source version predates required 4.13 workflow control-flow baseline')
else:
    audit=json.loads(text('release/feature-audit-660.json') or '[]')
    items=audit if isinstance(audit,list) else audit.get('features',[])
    by={int(str(x.get('id')).lstrip('#')):x for x in items if str(x.get('id','')).lstrip('#').isdigit()}
    for i in (424,432):
        if by.get(i,{}).get('status')!='Done': errors.append(f'feature #{i} regressed below Done after 4.13')
if version.get('semver') == '4.13.0' and version.get('msixVersion') != '4.13.0.0':
    errors.append('release/version.json 4.13 msix mismatch')


# 4.13 release truth markers
if version.get('semver') == '4.13.0':
    expected_counts={'Done':435,'Partial':61,'Foundation':106,'Missing':36,'ReleaseTest':22}
    audit=json.loads(text('release/feature-audit-660.json') or '{}')
    counts=audit.get('counts',{}) if isinstance(audit,dict) else {}
    if counts != expected_counts: errors.append(f'4.13 audit counts mismatch: {counts}')
    if audit.get('sourceVersion') != '4.13.0': errors.append('4.13 audit sourceVersion mismatch')
    appproj=text('src/Magic.Capture.App/Magic.Capture.App.csproj')
    manifest=text('src/Magic.Capture.App/Package.appxmanifest')
    for marker in ['<Version>4.13.0</Version>','<AssemblyVersion>4.13.0.0</AssemblyVersion>','<FileVersion>4.13.0.0</FileVersion>']:
        if marker not in appproj: errors.append(f'4.13 app csproj missing {marker}')
    if 'Version="4.13.0.0"' not in manifest: errors.append('4.13 manifest version mismatch')
    if '# Magic Capture Desktop 4.13' not in text('README.md'): errors.append('README missing 4.13 heading')
    if '# Magic Capture Desktop 4.13.0 — Feature Matrix' not in text('docs/FEATURE_MATRIX.md'): errors.append('Feature Matrix missing 4.13 heading')
    checklist=text('docs/WINDOWS_RELEASE_CHECKLIST.md')
    for marker in ['# Magic Capture Desktop 4.13.0 — Windows Release Checklist','435 `Done`, 61 `Partial`, 106 `Foundation`, 36 `Missing` and 22 `ReleaseTest`','WorkflowV5Tests','33 loop images']:
        if marker not in checklist: errors.append(f'4.13 Windows checklist missing {marker}')
    notes=text('docs/RELEASE_NOTES_4.13.0.md')
    for marker in ['ForEachImage','Resume failed workflow','435 Done / 61 Partial / 106 Foundation / 36 Missing / 22 ReleaseTest']:
        if marker not in notes: errors.append(f'4.13 release notes missing {marker}')

print('Magic Capture Desktop workflow control-flow source contract')
print(f'  Errors: {len(errors)}')
for e in errors: print('  -',e)
sys.exit(1 if errors else 0)
