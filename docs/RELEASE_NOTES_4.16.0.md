# Magic Capture Desktop 4.16.0 — Work Recovery

4.16.0 completes crash-recovery source coverage for every current durable project-backed editor without adding a cloud service, account dependency, resident polling worker, or heavyweight idle runtime.

## What changed

- Added `Magic.Capture.Core.Recovery.WorkspaceRecoveryPolicy`, a deterministic shared contract for Documentation and Video Edit recovery journals.
- Added crash-safe `DocumentationRecoveryStore` for `.magicdoc` snapshots and `VideoEditRecoveryStore` for `.magicclip` snapshots.
- Both stores use immutable revision snapshots followed by atomic journal promotion. A crash between those stages leaves the previous complete journal/snapshot pair recoverable.
- Recovery is bounded to eight active sessions per kind, 64 KiB journals, a fourteen-day lifetime and five-minute future-clock tolerance. Snapshot names bind kind + session + revision and recovery roots are separate under LocalAppData.
- Journals store only bounded recovery metadata and a display name. They do not store the user's original full project path.
- Documentation Builder now autosaves durable changes after a one-shot 1.5-second debounce, including project metadata, templates, steps, reorder operations and embedded logo/image state.
- Video Editor now autosaves project mutations, Undo/Redo and output-dimension edits with the same bounded debounce. Future-schema projects remain read-only and cannot enter recovery writes.
- Explicit save, project open and normal editor close are protected by revision counters, generation tokens and a per-window async write gate so stale autosave completions cannot erase newer work.
- Home now surfaces independent Documentation and Video Edit recovery cards with Recover and Discard actions. Recover opens a copy with no original save path; normal project files are never silently overwritten.
- Added `scripts/verify-work-recovery.py` to the reproducible source-release gate and added Core policy tests for kind isolation, path safety, time bounds, deduplication, ordering and session caps.

## Feature ledger

The exact 660-feature ledger is now:

- Done: **464**
- Partial: **46**
- Foundation: **92**
- Missing: **36**
- ReleaseTest: **22**
- Total: **660**

Promoted to Done in 4.16.0:

- #606 — Autosave editor project
- #607 — Restore editor after crash
- #608 — Restore unfinished document

#609 — Restore unfinished recording remains **Partial**. The existing interrupted-session detection does not reconstruct or finalize a partial MP4, so it is intentionally not promoted.

## Verification status

The Linux source-generation environment can run repository, structural, lexical and source-contract verifiers. Real .NET/xUnit execution, WinUI/XAML compilation, x64 and ARM64 Release builds, MSIX packaging, kill/relaunch recovery fault injection and mixed Windows runtime tests remain mandatory release gates in `docs/WINDOWS_RELEASE_CHECKLIST.md` because the Windows/.NET toolchain is not installed in this environment.
