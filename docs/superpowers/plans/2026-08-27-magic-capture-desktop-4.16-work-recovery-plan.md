# Magic Capture Desktop 4.16 Work Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** add bounded crash-safe recovery for Documentation Builder and Video Editor, completing recovery across all current durable project-backed editors.

**Architecture:** share deterministic recovery-journal policy in Core, keep format-specific persistence in two typed App stores, and reuse the existing 1.5-second revision/generation lifecycle pattern already proven by AnnotationWindow. Recovery remains local and lazy with no new resident worker.

**Tech Stack:** .NET 10, C#, WinUI 3, DispatcherQueueTimer, atomic JSON/file persistence, `.magicdoc` ZIP packages, `.magicclip` JSON projects.

**Spec:** `docs/superpowers/specs/2026-08-27-magic-capture-desktop-4.16-work-recovery-design.md`

## Global Constraints
- No cloud/backend/account requirement.
- No always-on background worker or idle polling loop.
- Recovery never overwrites the user's original project path.
- Recovery snapshots must be written through existing authoritative project stores.
- Maximum 8 active sessions per recovery kind, 64 KiB journal, 14-day lifetime, 5-minute future skew.
- Preserve `OperationCanceledException` and fail soft on non-fatal cleanup errors.
- Do not promote recording recovery (#609).

---

### Task 1: Shared deterministic recovery contract

**Files:**
- Create: `src/Magic.Capture.Core/Recovery/WorkspaceRecoveryPolicy.cs`
- Create: `tests/Magic.Capture.Core.Tests/WorkspaceRecoveryPolicyTests.cs`
- Create: `scripts/verify-work-recovery.py`

**Interfaces:**
- Produces: `WorkspaceRecoveryKind`, `WorkspaceRecoveryJournal`, `WorkspaceRecoveryCandidate`, `WorkspaceRecoveryValidationResult`, `WorkspaceRecoveryPolicy.Validate`, `SelectCandidates`, `BuildSnapshotFileName`, `IsSafeSnapshotFileName`.

- [x] Write Core tests for valid/invalid names, kind-extension mismatch, age/skew, session cap/order and display-name limit.
- [x] Add a source verifier that initially fails until both typed stores/window integrations exist.
- [x] Run the verifier and confirm RED due to missing 4.16 production files.
- [x] Implement the minimal Core policy.
- [x] Keep the xUnit tests as Windows/.NET execution gates and run available static verifiers.

### Task 2: Documentation Builder recovery

**Files:**
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Create: `src/Magic.Capture.App/Documentation/DocumentationRecoveryStore.cs`
- Modify: `src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces: `DocumentationRecoveryItem`; `SaveAsync`, `ListAsync`, `LoadAsync`, `DeleteAsync` on `DocumentationRecoveryStore`; recovered-window constructor in `DocumentationWindow`.

- [x] Add kind-specific LocalAppData recovery root and service registration.
- [x] Implement snapshot-first/journal-second typed store using `DocumentationProjectStore` for both save and load.
- [x] Add revision/generation/debounce lifecycle to DocumentationWindow and route all durable mutations through `ScheduleRecoveryAutosave`.
- [x] Make explicit save/open/close recovery-safe and race-safe.
- [x] Add Home recovery card with Recover/Discard and App window opener.
- [x] Run `verify-work-recovery.py` and existing static gates.

### Task 3: Video Editor recovery

**Files:**
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditRecoveryStore.cs`
- Modify: `src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces: `VideoEditRecoveryItem`; `SaveAsync`, `ListAsync`, `LoadAsync`, `DeleteAsync` on `VideoEditRecoveryStore`; recovered-window constructor in `VideoEditorWindow`.

- [x] Register video recovery root/store and keep media payloads out of recovery.
- [x] Add revision/generation/debounce lifecycle around project mutations, undo/redo, explicit save/open and close.
- [x] Keep future-schema projects read-only and ineligible for autosave writes.
- [x] Add Home recovery card with Recover/Discard and recovered-window opener.
- [x] Run `verify-work-recovery.py` and all static gates.

### Task 4: Release truth and reproducible source package

**Files:**
- Modify: `scripts/source-release.py`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_4.16.0.md`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `README.md`

**Interfaces:**
- Produces: version 4.16.0 source package and checksum.

- [x] Add work-recovery verifier to source packaging gate.
- [x] Promote only #606, #607 and #608 if source evidence is complete; keep #609 Partial.
- [x] Update feature totals and release docs consistently.
- [x] Update semver/MSIX versions to 4.16.0 / 4.16.0.0.
- [ ] Run all source verifiers twice, generate two source ZIPs, compare SHA-256 for reproducibility, then verify final ZIP contents.
- [x] Record Windows/.NET runtime gates as unexecuted when the toolchain is unavailable.
