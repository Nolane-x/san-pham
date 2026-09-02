# History Intelligence & Organization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Ship Magic Capture Desktop 4.14 with bounded local History organization, activity filters, most-used sorting, timeline navigation, process metadata/icons, and drag/drop import.

**Architecture:** Keep the primary `HistoryItem` image index authoritative for capture durability and persist organization/activity in a separate atomic `history-library.json`. Core owns models/policy/query semantics; App owns storage, icon extraction, activity instrumentation and WinUI management. No background worker or cloud service is introduced.

**Tech Stack:** .NET 10, C#, WinUI 3, System.Text.Json, System.Drawing, existing AtomicJsonFile/HistoryStore/source verifiers.

**Spec:** `docs/superpowers/specs/2026-08-26-history-intelligence-organization-design.md`

## Global Constraints
- Preserve source compatibility with existing History index JSON.
- Never persist OCR/AI/prompt/output payloads in library activity metadata.
- Maximum 32 workspaces, 128 folders, 128 collections, 500 imported files per drop.
- Dropped folders are top-level only; no recursive crawl.
- Organizer deletion must never delete captures.
- Icon extraction is best-effort only.
- Only feature rows #258, #259, #260, #272, #273, #275, #280, #285, #286, #287, #451 and #454 may change status.

---

### Task 1: Core organizer/activity model and policy
**Files:**
- Create: `src/Magic.Capture.Core/History/HistoryLibrary.cs`
- Modify: `src/Magic.Capture.Core/History/HistoryQuery.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistoryLibraryTests.cs`
- Create: `scripts/verify-history-intelligence.py`

**Interfaces:**
- Produces `HistoryLibrarySnapshot`, workspace/folder/collection/activity records, `HistoryLibraryPolicy.Normalize/Validate`, and extended `HistoryQuery.Apply(..., HistoryLibrarySnapshot?)`.

- [x] Write the source-contract assertions and xUnit source for bounds, normalization, membership pruning, activity saturation, filters and MostUsed ordering.
- [x] Run `python3 scripts/verify-history-intelligence.py` and confirm RED because 4.14 symbols do not exist.
- [x] Implement the Core model/policy/query changes.
- [x] Run history contract, repo, structure and lexical verifiers and confirm GREEN.

### Task 2: Atomic HistoryLibraryStore and lifecycle cleanup
**Files:**
- Create: `src/Magic.Capture.App/Persistence/HistoryLibraryStore.cs`
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/Persistence/HistoryStore.cs`

**Interfaces:**
- Produces load/save/create/rename/delete/assign/record-activity APIs and best-effort prune by capture IDs.

- [x] Extend history contract for atomic store, 32 MiB cap, quarantine and DI wiring; verify RED.
- [x] Implement store and AppPaths/DI wiring.
- [x] Prune organizer references after successful History delete/clear/retention transactions without making capture deletion depend on organizer I/O.
- [x] Re-run all static gates and confirm GREEN.

### Task 3: Workflow and AI activity instrumentation
**Files:**
- Modify: `src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/Ai/MagicActionExecutionRequest.cs` or equivalent request model/call sites if needed.

**Interfaces:**
- Records workflow/action identifiers by History asset ID without payloads.

- [x] Extend source contract for workflow and AI activity calls; verify RED.
- [x] Record workflow activity for single, batch, loop and resume History execution paths without making activity persistence authoritative for execution result.
- [x] Record direct History-backed Magic Action IDs only when an asset ID is known.
- [x] Re-run gates and privacy scan for forbidden payload fields.

### Task 4: Executable metadata and icon cache
**Files:**
- Modify: `src/Magic.Capture.App/Capture/CaptureAsset.cs`
- Modify: `src/Magic.Capture.App/Capture/WindowCaptureService.cs`
- Modify: `src/Magic.Capture.Core/History/HistoryItem.cs`
- Modify: `src/Magic.Capture.App/Persistence/HistoryStore.cs`
- Create: `src/Magic.Capture.App/Persistence/HistoryProcessIconCache.cs`
- Modify: `src/Magic.Capture.App/ViewModels/HistoryDisplayItem.cs`

**Interfaces:**
- Adds optional `ExecutablePath` through capture -> history, and best-effort icon URI/BitmapImage for History rows.

- [x] Add contract/xUnit source for path bounds/backward-null behavior; verify RED.
- [x] Resolve executable path best-effort for Window captures and persist bounded metadata.
- [x] Add bounded icon cache keyed by normalized executable path hash; icon extraction failures return null.
- [x] Surface process icon in display item without blocking capture durability.
- [x] Re-run gates.

### Task 5: History organization/filter/timeline UI and drag/drop
**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Create: `src/Magic.Capture.App/Views/HistoryLibraryManagerWindow.xaml`
- Create: `src/Magic.Capture.App/Views/HistoryLibraryManagerWindow.xaml.cs`

**Interfaces:**
- History organization selectors, manager, activity filters, MostUsed sort, timeline mode, file/folder drag/drop import.

- [x] Extend source contract for UI handlers and drag/drop caps; verify RED.
- [x] Add History organization controls and manager window.
- [x] Wire Workspace/Folder/Collection filters and assignments to selected captures.
- [x] Add Workflow/AI filters and MostUsed sort.
- [x] Add timeline view grouped by local day.
- [x] Add image/folder drag/drop with supported-extension validation, top-level-only folder enumeration and 500-file cap.
- [x] Re-run XAML structural and lexical gates.

### Task 6: Release truth and deterministic 4.14 source package
**Files:**
- Modify: `release/version.json`
- Modify: `release/feature-audit-660.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `README.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_4.14.0.md`
- Modify: `scripts/source-release.py`

**Interfaces:**
- Source version 4.14.0 / MSIX 4.14.0.0 and deterministic source artifact.

- [x] Compare 4.14 audit against extracted 4.13 final and assert only scoped 12 rows change.
- [x] Promote scoped rows only after code/UI evidence exists and render feature matrix.
- [x] Update version/docs/checklist/release notes and source-release verifier chain.
- [x] Run all static verifiers from the source tree.
- [x] Package provisional A/B and prove byte-identical.
- [x] Extract provisional and rerun all gates/audit/version checks.
- [x] Mark packaging steps complete, package final A2/B2, prove byte-identical, then verify the exact delivery ZIP and checksum sidecar.
