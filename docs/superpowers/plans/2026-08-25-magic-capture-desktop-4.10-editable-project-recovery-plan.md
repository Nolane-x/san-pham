# Magic Capture Desktop 4.10 Editable Project Autosave Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded, crash-safe autosave recovery for editable `.magiccapture` annotation projects without overwriting user project files.

**Architecture:** Core owns recovery-policy validation and deterministic candidate selection. App owns an atomic LocalAppData recovery store, AnnotationWindow owns session-scoped debounce/dirty lifecycle, and MainWindow owns explicit Recover/Discard startup UX.

**Tech Stack:** .NET 10, WinUI 3, System.Text.Json, ZipArchive through existing `EditableProjectService`, Python source verifiers.

**Spec:** `docs/superpowers/specs/2026-08-25-magic-capture-desktop-4.10-editable-project-recovery-design.md`

## Global Constraints
- Local-first only; no cloud/backend dependency.
- No resident autosave worker; timer exists only while an AnnotationWindow is alive and dirty.
- Maximum 8 active recovery sessions, 64 KiB journal, 14-day recovery lifetime.
- Recovery snapshots must reuse `.magiccapture` archive validation and bounds.
- Never overwrite the user's original project during recovery.
- Do not promote unrelated feature-audit rows.

---

### Task 1: Recovery core policy and tests
**Files:**
- Create: `src/Magic.Capture.Core/Projects/EditableProjectRecoveryPolicy.cs`
- Create: `tests/Magic.Capture.Core.Tests/EditableProjectRecoveryPolicyTests.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Produces `EditableProjectRecoveryJournal`, `EditableProjectRecoveryCandidate`, and `EditableProjectRecoveryPolicy.Validate/SelectCandidates`.

- [x] Add failing source-contract checks requiring the policy file, journal schema, 8-session cap, 14-day lifetime, basename-only `.magiccapture` snapshot validation, and candidate ordering.
- [x] Run `python3 scripts/verify-repo.py` and confirm failure is caused by the missing recovery implementation.
- [x] Implement the pure Core policy and xUnit tests for valid/invalid journals, expiry, future timestamps, unsafe names and newest-first cap.
- [x] Run repository/structure/lexical verifiers and require zero errors.

### Task 2: Atomic LocalAppData recovery store
**Files:**
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Create: `src/Magic.Capture.App/Persistence/EditableProjectRecoveryStore.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Produces `SaveAsync`, `ListAsync`, `LoadAsync`, `DeleteAsync`, and `PruneAsync` for recovery sessions.
- Consumes existing `EditableProjectService.SaveAsync/LoadAsync`.

- [x] Add failing source-contract checks for the recovery root, generated GUID names, temp/promote journal writes, bounded journal reads, reuse of `EditableProjectService`, and service registration.
- [x] Confirm the verifier fails before production code exists.
- [x] Implement the store with serialized multi-editor access, bounded pruning/quarantine, stale-temp aging, and no alternate snapshot parser.
- [x] Re-run all static gates.

### Task 3: AnnotationWindow debounced autosave lifecycle
**Files:**
- Modify: `src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Creates one recovery session id per editor window.
- Debounces dirty revisions for 1500 ms using a `DispatcherQueueTimer`.

- [x] Add failing checks for committed-mutation dirty marking, timer interval, undo/redo autosave, race-safe explicit-save cleanup, generation invalidation, open-project reset/suppression, and clean-close cleanup.
- [x] Confirm RED.
- [x] Implement session-scoped debounce and recovery generations; all autosave errors are logged and never block editing.
- [x] Re-run all static gates.

### Task 4: Startup Recover/Discard UX
**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- MainWindow `RefreshEditableProjectRecoveryAsync`, `RecoverEditableProject_Click`, `DiscardEditableProjectRecovery_Click`.
- App method opens an AnnotationWindow from an `EditableProjectPackage` recovery snapshot without writing an original project path.

- [x] Add failing checks for a collapsed recovery card, recover/discard handlers, candidate refresh during service attachment, package-to-editor open path, and invalid-only quarantine.
- [x] Confirm RED.
- [x] Implement UX and safe error messages; successful recovery keeps the same session alive in the recovered editor, while transient open errors keep the candidate for retry.
- [x] Run structural verifier to prove new XAML handlers resolve.

### Task 5: Release truth, checklist and deterministic package
**Files:**
- Modify: `release/version.json`, app project/manifest version sources, `docs/FEATURE_AUDIT_660.md`, audit JSON artifacts, `docs/FEATURE_MATRIX.md`, `docs/WINDOWS_RELEASE_CHECKLIST.md`, `README.md`.
- Create: `docs/RELEASE_NOTES_4.10.0.md`.

**Interfaces:**
- Source release `4.10.0 / 4.10.0.0` only if #254 is end-to-end wired.

- [x] Promote only #254 from Missing to Done after code/UI/store wiring is present; leave #606 unchanged unless its row evidence is genuinely satisfied.
- [x] Re-render/validate the 660-row audit and assert counts sum to 660.
- [x] Run repository, structural and lexical verifiers fresh.
- [x] Build deterministic source ZIP twice, compare SHA-256, `testzip`, extract the final ZIP and rerun all verifiers inside the extracted package.
