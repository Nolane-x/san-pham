#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
errors = []

def text(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append(f"missing file: {rel}")
        return ""
    return path.read_text(encoding="utf-8")

def need(rel, *markers):
    body = text(rel)
    for marker in markers:
        if marker not in body:
            errors.append(f"{rel}: missing marker {marker!r}")

def forbid(rel, *markers):
    body = text(rel)
    for marker in markers:
        if marker in body:
            errors.append(f"{rel}: forbidden marker {marker!r}")

need('src/Magic.Capture.Core/Recovery/WorkspaceRecoveryPolicy.cs',
     'enum WorkspaceRecoveryKind', 'Documentation', 'VideoEdit',
     'MaximumActiveSessions = 8', 'MaximumJournalBytes = 64L * 1024',
     'MaximumAge = TimeSpan.FromDays(14)', 'MaximumFutureClockSkew = TimeSpan.FromMinutes(5)',
     'BuildSnapshotFileName', 'IsSafeSnapshotFileName', 'SelectCandidates')
need('tests/Magic.Capture.Core.Tests/WorkspaceRecoveryPolicyTests.cs',
     'Snapshot_name_is_kind_scoped_and_safe', 'Candidate_selection_is_kind_filtered_newest_first_deduplicated_and_bounded')
need('src/Magic.Capture.App/Persistence/AppPaths.cs',
     'DocumentationRecoveryRoot', 'VideoEditRecoveryRoot', 'recovery", "documentation', 'recovery", "video-edit')
need('src/Magic.Capture.App/Documentation/DocumentationRecoveryStore.cs',
     'DocumentationProjectStore', 'WorkspaceRecoveryPolicy', 'SaveAsync(', 'ListAsync(', 'LoadAsync(', 'DeleteAsync(',
     'File.Move(tempJournal, journalPath, overwrite: true)', 'MaximumJournalBytes')
need('src/Magic.Capture.App/VideoEditing/VideoEditRecoveryStore.cs',
     'VideoEditProjectStore', 'WorkspaceRecoveryPolicy', 'SaveAsync(', 'ListAsync(', 'LoadAsync(', 'DeleteAsync(',
     'File.Move(tempJournal, journalPath, overwrite: true)', 'MaximumJournalBytes')
forbid('src/Magic.Capture.Core/Recovery/WorkspaceRecoveryPolicy.cs', 'OriginalProjectPath', 'FullPath')

need('src/Magic.Capture.App/ApplicationServices.cs', 'DocumentationRecovery', 'VideoEditRecovery')
need('src/Magic.Capture.App/App.xaml.cs',
     'new DocumentationRecoveryStore', 'new VideoEditRecoveryStore',
     'OpenRecoveredDocumentationProject', 'OpenRecoveredVideoEditProject')
need('src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs',
     'DispatcherQueueTimer _recoveryTimer', 'ScheduleRecoveryAutosave', 'FlushRecoveryAutosaveAsync',
     'HandleExplicitSaveSucceededAsync', 'InvalidateAndDeleteRecoveryAsync', 'DocumentationRecovery.SaveAsync',
     'IsExitRequested')
need('src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs',
     'DispatcherQueueTimer _recoveryTimer', 'ScheduleRecoveryAutosave', 'FlushRecoveryAutosaveAsync',
     'HandleExplicitSaveSucceededAsync', 'InvalidateAndDeleteRecoveryAsync', 'VideoEditRecovery.SaveAsync',
     'IsExitRequested')
need('src/Magic.Capture.App/MainWindow.xaml',
     'DocumentationRecoveryCard', 'RecoverDocumentationProject_Click', 'DiscardDocumentationRecovery_Click',
     'VideoEditRecoveryCard', 'RecoverVideoEditProject_Click', 'DiscardVideoEditRecovery_Click')
need('src/Magic.Capture.App/MainWindow.xaml.cs',
     'RefreshDocumentationRecoveryAsync', 'RefreshVideoEditRecoveryAsync',
     'RecoverDocumentationProject_Click', 'DiscardDocumentationRecovery_Click',
     'RecoverVideoEditProject_Click', 'DiscardVideoEditRecovery_Click')


need('scripts/source-release.py', 'verify-work-recovery.py', '2026, 8, 27')
need('.github/workflows/windows-ci.yml', 'verify-work-recovery.py')
need('src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs', 'ProjectMetadata_TextChanged', 'ScheduleRecoveryAutosave();')
need('src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs', 'OutputDimensions_ValueChanged', 'ScheduleRecoveryAutosave();')
forbid('src/Magic.Capture.App/Documentation/DocumentationRecoveryStore.cs', 'OriginalProjectPath')
forbid('src/Magic.Capture.App/VideoEditing/VideoEditRecoveryStore.cs', 'OriginalProjectPath')

print('Magic Capture Desktop work recovery verifier')
print(f'  Errors: {len(errors)}')
for error in errors:
    print('  -', error)
raise SystemExit(1 if errors else 0)
