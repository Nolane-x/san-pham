# Magic Capture Desktop 4.10.0 — Editable Project Recovery

Magic Capture Desktop 4.10.0 is a reliability wave for the editable `.magiccapture` annotation workflow. It adds bounded local autosave recovery without introducing a cloud service, background resident worker, alternate project parser, or automatic overwrite of user project files; recovery never overwrites the original project path.

## Crash-safe local autosave

- Annotation editor mutations now schedule a **1.5-second debounced recovery snapshot**. The timer exists only for the editor window and does not create a resident application worker.
- Recovery snapshots live under `%LOCALAPPDATA%/Magic Capture Desktop/recovery/editable-projects` and are ordinary `.magiccapture` packages written through the existing `EditableProjectService`.
- The recovery journal is separate from the snapshot and contains only bounded session metadata: ids, timestamps, dimensions, revision, generated snapshot file name and an optional display name.
- A successful explicit project save clears recovery only when no newer edit was made after the state sent to disk. If the user edits while Save is still completing, the newer dirty revision keeps/restarts recovery instead of being erased.
- Opening another project invalidates the previous recovery generation before replacing state; normal editor close performs best-effort cleanup. Whole-app exit preserves the last completed unsaved recovery rather than deleting it during shutdown.
- Undo, redo, layer edits, drawing operations, local Smart Redact and destructive transforms all enter the same debounced recovery lifecycle through the editor mutation boundary.

## Recovery validation and safety

`EditableProjectRecoveryPolicy` introduces a schema-1 journal contract with hard limits: at most **8** active sessions, **64 KiB** per journal and **14 days** of lifetime. Snapshot names are strict basename-only `sessionId-dirtyRevision.magiccapture` values: a 32-hex session id plus a fixed 20-digit positive revision, so a journal cannot claim another session or revision's snapshot.

The recovery store reuses the existing `.magiccapture` archive/PNG/schema validation instead of parsing a looser recovery format. Each revision first writes an immutable revision-scoped snapshot and only then atomically promotes the journal pointer; the previous snapshot is deleted only after the new pointer is durable, so a process death between the two promotions still leaves one complete recoverable pair. The store bounds journal reads, checks snapshot package size before load, re-reads and value-compares the full journal immediately before recovery, verifies project id and dimensions against the snapshot, prunes expired/invalid entries and removes orphaned recovery files on a bounded pass. A store-wide serialization gate prevents one editor's prune from racing another editor's snapshot/journal promotion; stale temporary files are only removed after an age threshold.

## Explicit recovery UX

When a valid local recovery candidate exists, Control Center Home shows an **Unsaved editor work found** card with `Recover` and `Discard` actions. Recover opens the autosave in a new editor and does **not** write to the original user project path. A candidate that fails final data validation is removed and reported with a generic local warning rather than trusted file content; transient I/O/access failures keep the recovery for a later retry instead of deleting it.

A recovered editor keeps the same recovery session until it is explicitly saved or closed, so another crash before the user saves does not intentionally discard the last recovery snapshot.

## Release truth

The exact 660-entry audit is now **416 Done / 64 Partial / 122 Foundation / 36 Missing / 22 ReleaseTest**. Only feature **#254 Autosave recovery** moves from Missing to Done. Feature #606 remains Foundation because this wave does not claim a universal autosave system for every editor subsystem.

## Verification boundary

The source-level repository, XAML-structure and C# lexical gates are required for this bundle. The current Linux environment does not provide `dotnet`, `msbuild` or the Windows/WinUI SDK, so xUnit execution, x64/ARM64 compilation, XAML compilation, MSIX packaging and real crash/kill/relaunch behavior remain mandatory Windows release gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
