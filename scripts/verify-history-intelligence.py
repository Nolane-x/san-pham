#!/usr/bin/env python3
from pathlib import Path
import json,sys
ROOT=Path(__file__).resolve().parents[1]
errors=[]
def text(rel):
 p=ROOT/rel
 if not p.exists(): errors.append(f'missing file: {rel}'); return ''
 return p.read_text(encoding='utf-8')
def need(rel,*needles):
 s=text(rel)
 for n in needles:
  if n not in s: errors.append(f'{rel}: missing {n}')
 return s

core=need('src/Magic.Capture.Core/History/HistoryLibrary.cs',
 'HistoryLibrarySnapshot','HistoryWorkspace','HistoryFolder','HistoryCollection','HistoryAssetLibraryRecord',
 'MaximumWorkspaces = 32','MaximumFolders = 128','MaximumCollections = 128','MaximumCollectionMembers = 5000',
 'MaximumCollectionsPerAsset = 32','MaximumWorkflowIdsPerAsset = 32','MaximumAiActionIdsPerAsset = 32','MaximumUseCount = 1000000',
 'HistoryLibraryPolicy')
query=need('src/Magic.Capture.Core/History/HistoryQuery.cs',
 'MostUsed','WorkspaceId','FolderId','CollectionId','WorkflowId','AiActionId','HistoryLibrarySnapshot')
store=need('src/Magic.Capture.App/Persistence/HistoryLibraryStore.cs',
 'MaximumLibraryJsonBytes = 32L * 1024 * 1024','AtomicJsonFile','RecordOpenedAsync','RecordWorkflowAsync','RecordAiActionAsync',
 'AssignWorkspaceFolderAsync','SetCollectionMembershipAsync','PruneAssetsBestEffortAsync')
paths=need('src/Magic.Capture.App/Persistence/AppPaths.cs','HistoryLibraryFile')
services=need('src/Magic.Capture.App/ApplicationServices.cs','HistoryLibrary')
app=need('src/Magic.Capture.App/App.xaml.cs','HistoryLibraryStore')
asset=need('src/Magic.Capture.App/Capture/CaptureAsset.cs','ExecutablePath')
window=need('src/Magic.Capture.App/Capture/WindowCaptureService.cs','TryGetExecutablePath')
item=need('src/Magic.Capture.Core/History/HistoryItem.cs','ExecutablePath')
icons=need('src/Magic.Capture.App/Persistence/HistoryProcessIconCache.cs','ExtractAssociatedIcon','SHA256.HashData')
display=need('src/Magic.Capture.App/ViewModels/HistoryDisplayItem.cs','ProcessIcon')
mainx=need('src/Magic.Capture.App/MainWindow.xaml','HistoryLibraryManager_Click','History_DragOver','History_Drop','HistoryTimelineList')
main=need('src/Magic.Capture.App/MainWindow.xaml.cs',
 'HistoryLibraryManager_Click','History_DragOver','History_Drop','HistoryTimeline','MostUsed','WorkflowId','AiActionId','MaximumDroppedFiles = 500')
managerx=need('src/Magic.Capture.App/Views/HistoryLibraryManagerWindow.xaml','HistoryLibraryManagerWindow')
manager=need('src/Magic.Capture.App/Views/HistoryLibraryManagerWindow.xaml.cs','CreateWorkspace_Click','CreateFolder_Click','CreateCollection_Click','AssignSelected_Click')
workflow=need('src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs','RecordWorkflowAsync')
# At least one direct History workflow path in MainWindow should record activity too.
if 'RecordWorkflowAsync' not in main: errors.append('MainWindow: missing History workflow activity recording')
# AI activity may be recorded from the direct History AI context/magic-action host path.
if 'RecordAiActionAsync' not in main and 'RecordAiActionAsync' not in app: errors.append('History AI action activity recording missing')
source_release=need('scripts/source-release.py','verify-history-intelligence.py')

# 4.14 hardening contracts: organization changes must be transactionally visible,
# activity persistence must never block primary History operations, and filters must
# reflect actions that actually reached workflow execution.
for marker in [
 'selectedWorkspaceId',
 '_historyQueryOptions = _historyQueryOptions with { WorkspaceId = selectedWorkspaceId, FolderId = null }',
 'RecordHistoryOpenedBestEffortAsync',
 'RecordAiActionsBestEffortAsync',
 'WorkflowStepStatus.Skipped',
 'WorkflowStepStatus.WouldRun']:
 if marker not in main and marker not in workflow: errors.append(f'history hardening: missing {marker}')
for marker in [
 'HistoryLibraryReadTransient',
 'MaximumCollectionMembers',
 'Collection member limit reached']:
 if marker not in store: errors.append(f'history store hardening: missing {marker}')
if 'PruneAssetsBestEffortAsync(removedIds, CancellationToken.None)' not in text('src/Magic.Capture.App/Persistence/HistoryStore.cs'):
 errors.append('history prune hardening: post-commit cleanup must use CancellationToken.None')
if 'ExecutablePath: source.ExecutablePath' not in text('src/Magic.Capture.App/Persistence/HistoryStore.Resilience.cs'):
 errors.append('portable History import must preserve ExecutablePath metadata')
if 'MaximumIconPngBytes' not in icons:
 errors.append('process icon cache must cap encoded PNG size')
if 'IsLocalWindowsExecutablePath' not in icons:
 errors.append('process icon cache must reject non-local/UNC executable paths')
if 'OrderByDescending(display => display.CreatedUtc)' not in main:
 errors.append('timeline view must enforce chronological descending order independent of list sort')
if 'HistoryLibraryReadTransient' not in store:
 errors.append('history library public reads must fail-soft on transient I/O')
load_unsafe = store.split('private async Task<HistoryLibrarySnapshot> LoadUnsafeAsync',1)[1] if 'private async Task<HistoryLibrarySnapshot> LoadUnsafeAsync' in store else ''
if 'HistoryLibraryLoadTransient' in load_unsafe or 'HistoryLibraryReadTransient' in load_unsafe:
 errors.append('history library mutation load path must propagate transient I/O instead of returning Empty')

# Privacy: history library cannot persist payload-like fields.
for forbidden in ['PngBytes','OcrText','PromptText','PromptAnswer','HttpBody','Stdout','Stderr','AiResponse','Markdown']:
 if forbidden in core or forbidden in store: errors.append(f'history library privacy: forbidden field/token {forbidden}')

version=json.loads(text('release/version.json') or '{}')
if version.get('semver')=='4.14.0':
 audit=json.loads(text('release/feature-audit-660.json') or '{}')
 expected={'Done':447,'Partial':60,'Foundation':95,'Missing':36,'ReleaseTest':22}
 if audit.get('counts')!=expected: errors.append(f'4.14 audit counts mismatch: {audit.get("counts")}')
 if audit.get('sourceVersion')!='4.14.0': errors.append('4.14 audit sourceVersion mismatch')
 by={int(x['id']):x for x in audit.get('features',[])}
 for i in (258,259,260,272,273,275,280,285,286,287,451,454):
  if by.get(i,{}).get('status')!='Done': errors.append(f'feature #{i} not Done in 4.14')
 if version.get('msixVersion')!='4.14.0.0': errors.append('4.14 msixVersion mismatch')
 for marker in ['<Version>4.14.0</Version>','<AssemblyVersion>4.14.0.0</AssemblyVersion>','<FileVersion>4.14.0.0</FileVersion>']:
  if marker not in text('src/Magic.Capture.App/Magic.Capture.App.csproj'): errors.append(f'app csproj missing {marker}')
 if 'Version="4.14.0.0"' not in text('src/Magic.Capture.App/Package.appxmanifest'): errors.append('manifest not 4.14.0.0')
 for rel,marker in [('README.md','# Magic Capture Desktop 4.14'),('docs/FEATURE_MATRIX.md','# Magic Capture Desktop 4.14.0 — Feature Matrix'),('docs/RELEASE_NOTES_4.14.0.md','History Intelligence')]:
  if marker not in text(rel): errors.append(f'{rel}: missing 4.14 marker')

print('Magic Capture Desktop history intelligence source contract')
print(f'  Errors: {len(errors)}')
for e in errors: print('  -',e)
sys.exit(1 if errors else 0)
