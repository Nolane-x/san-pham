# Magic Capture Desktop 4.10 Editable Project Autosave Recovery Design

## Goal
Protect unsaved `.magiccapture` annotation work from process crashes or forced shutdown without overwriting the user's chosen project file and without adding a resident background worker.

## Scope
This wave targets feature #254 `Autosave recovery` for the editable annotation project flow (#250–#255). It does not claim recording recovery, video-editor recovery, documentation-project recovery, or general application-session restoration. Feature #606 is promoted only if its existing definition is fully satisfied by the resulting annotation-editor autosave implementation; otherwise it remains Foundation.

## Architecture
`Magic.Capture.Core` owns a small recovery policy: schema/version validation, path-independent journal validation, expiration rules, maximum active sessions, revision-scoped snapshot naming, and deterministic ordering. `Magic.Capture.App` owns a recovery store under LocalAppData. Each recovery session consists of a bounded JSON journal plus normal `.magiccapture` snapshots written through `EditableProjectService`, so recovery reuses the same archive validation and image limits as user projects. Each dirty revision writes an immutable `session-revision.magiccapture` snapshot first; only after that file is fully promoted is the journal atomically replaced to point at it. The prior snapshot stays valid until the pointer promotion completes, eliminating the two-file crash window that could otherwise pair old journal metadata with a newer resized/cropped snapshot.

The annotation editor creates a stable recovery session id per window. A committed mutation marks the session dirty and restarts a UI-thread debounce timer. When the timer fires, the editor writes a recovery snapshot atomically. Recovery generation tokens invalidate stale in-flight autosaves across explicit save/open/close boundaries. Normal explicit save clears recovery only if no newer mutation occurred after the state that was written; otherwise the newer dirty revision remains recoverable. Clean editor-window close clears the entry, while whole-app exit leaves the last completed unsaved snapshot available. A crash likewise leaves the last completed snapshot and journal behind.

At application startup, MainWindow asks the store for valid candidates. Corrupt, missing, unsafe, oversized, future-schema, or expired candidates are quarantined/deleted rather than loaded. If a candidate exists, MainWindow shows a local recovery card with Recover and Discard actions. Recover loads the snapshot through `EditableProjectService` and opens a new AnnotationWindow that continues to own the same recovery session; MainWindow ignores that session for the rest of the current process so a second crash remains recoverable. Discard removes it. Recovery never overwrites the user's original file. Transient I/O/access failures do not delete otherwise valid recovery data.

## Data model and limits
- Recovery root: `%LOCALAPPDATA%/Magic Capture Desktop/recovery/editable-projects`.
- Maximum active recovery sessions: 8.
- Journal schema version: 1.
- Maximum journal size: 64 KiB.
- Maximum age: 14 days.
- Snapshot extension: `.magiccapture`.
- Journal stores only ids, timestamps, an optional original **display name** (never the original full path), snapshot file name, width/height and dirty revision; no image bytes and no OCR/text payload.
- Snapshot names are deterministic `32-hex-sessionId` + `-` + `20-digit-dirtyRevision` + `.magiccapture`; they must pass a strict basename/extension/session/revision policy and may not contain separators or traversal.
- Candidate enumeration is deterministic newest-first, bounded to 8.

## Lifecycle
1. New AnnotationWindow starts with no recovery file.
2. First successful mutation calls `MarkDirty`; the debounce timer waits 1500 ms.
3. Timer captures the editor state and calls `EditableProjectRecoveryStore.SaveAsync`, which promotes a revision-scoped snapshot and then atomically promotes the journal pointer.
4. Further mutations restart the timer and increment a revision; after a newer journal pointer is durable, the previous revision snapshot becomes pruneable.
5. Explicit Save Project snapshots the current dirty revision, writes the user's project, then clears recovery only if no later revision appeared while the save was in flight; otherwise recovery is retained/restarted for the newer edits.
6. Open Project suppresses autosave while invalidating the old recovery generation and replacing state, adopts the loaded manifest project id, then starts a new recovery session clean until the next mutation.
7. Clean editor-window closing cancels the first close request, invalidates/deletes recovery, then closes; a whole-app exit stops the timer but preserves the last completed unsaved recovery.
8. Crash/forced termination leaves the last completed atomic snapshot.
9. MainWindow startup exposes valid candidates; Recover opens one snapshot using the same recovery session and hides that session in-process until the recovered editor saves/closes or the process ends.

## Failure handling
- Recovery write failure is logged and leaves the previous valid snapshot intact.
- A store-wide async gate serializes Save/List/Load/Delete/Prune across editor windows so pruning cannot delete another editor's snapshot between snapshot promotion and journal promotion.
- Recovery cleanup failure is logged but must not block closing or explicit saves.
- Transient recovery-open failures are logged and retained for retry; only invalid/corrupt candidates are quarantined/deleted.
- A journal without its snapshot, a snapshot without a valid journal, duplicate candidate ids, future journal schema, invalid dimensions, unsafe file names, or expired timestamps is not recoverable.
- Snapshot loading always goes through `EditableProjectService.LoadAsync`; no alternate permissive parser is introduced.
- No exception text from recovery files is shown verbatim as trusted content in the MainWindow card.

## UI
A compact recovery card appears on Home only when a valid candidate exists. It shows the last autosave local time and optional original project display name. Buttons: `Recover` and `Discard`. After either action, the card refreshes to the next candidate or hides.

AnnotationWindow status may briefly say `Autosaved recovery` after a successful autosave, but autosave must not steal focus or open dialogs.

## Verification
- Core unit tests cover schema validation, unsafe snapshot names, future timestamps, expiration boundary, session cap and newest-first ordering.
- App source contract verifies LocalAppData path, atomic temp/promote behavior, same `EditableProjectService` load/save path, debounced mutation wiring, clean-save/clean-close cleanup, startup recover/discard UI, and no raw key/text capture.
- Existing repository, structural XAML-handler and C# lexical verifiers remain green.
- Windows release checklist adds crash-kill, save/close cleanup, corrupt journal/snapshot, expired recovery, multi-session cap and recovery-without-overwrite tests.
