# Magic Capture Desktop 4.8 Local Step Recorder & Documentation Builder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Build an explicit-session local Step Recorder that produces editable `.magicdoc` projects and exports them to long PNG, PDF, DOCX, HTML, Markdown and offline HTML.

**Architecture:** Deterministic project/crop/description/export contracts live in `Magic.Capture.Core.Documentation`. Windows hooks, capture/UIA, persistence, bitmap rendering and WinUI live in `Magic.Capture.App.Documentation` plus a dedicated `DocumentationWindow`.

**Tech Stack:** .NET 10, WinUI 3, Win32 low-level hooks, existing UI Automation/capture backends, System.IO.Compression, System.Drawing bitmap rendering, xUnit Core tests.

**Spec:** `docs/superpowers/specs/2026-08-25-magic-capture-desktop-4.8-step-recorder-design.md`

## Global Constraints
- Local-first; no backend/cloud dependency and no automatic AI calls.
- Session-scoped hooks only; no resident worker/polling.
- Never persist printable typed text or password-field values.
- Maximum 512 documentation steps and 512 MiB total packaged image payload.
- Unknown future `.magicdoc` schemas are not edited as current schemas.
- Long-image export remains below 150,000,000 decoded pixels.
- Existing capture backend router and UIA snapshot service remain authorities for screen acquisition/evidence.

---

### Task 1: Core documentation model and deterministic policy

**Files:**
- Create: `src/Magic.Capture.Core/Documentation/DocumentationModels.cs`
- Create: `src/Magic.Capture.Core/Documentation/DocumentationPolicy.cs`
- Create: `tests/Magic.Capture.Core.Tests/DocumentationPolicyTests.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Produces `DocumentationProject`, `DocumentationStep`, `DocumentationTargetEvidence`, `DocumentationMouseButton`.
- Produces `DocumentationPolicy.Normalize`, `PlanCapture`, `GenerateDescription`, `GenerateProjectTitle`, `MoveStep`, `RemoveStep`, `DuplicateStep`, `MergeSteps`, `IsSafeKeyboardGesture`.

- [x] Add a 4.8 source contract to `verify-repo.py` that requires the new Core files/types/tests and run it; expect RED because files do not exist.
- [x] Add xUnit tests covering capture clamping, click coalescing, UIA-target preference, deterministic descriptions, step operations, safe-key filtering and bounds.
- [x] Implement the Core records/policies minimally to satisfy the tests/source contract.
- [x] Run repository/structure/lexical gates and keep them green.

### Task 2: `.magicdoc` archive validation and Core document writers

**Files:**
- Create: `src/Magic.Capture.Core/Documentation/DocumentationArchivePolicy.cs`
- Create: `src/Magic.Capture.Core/Documentation/DocumentationTextExport.cs`
- Create: `src/Magic.Capture.Core/Documentation/DocumentationDocxWriter.cs`
- Create: `tests/Magic.Capture.Core.Tests/DocumentationArchivePolicyTests.cs`
- Create: `tests/Magic.Capture.Core.Tests/DocumentationTextExportTests.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Produces canonical archive validation for `manifest.json`, `steps/<guid>.png`, optional `logo.png`.
- Produces escaped HTML/Markdown and a deterministic DOCX ZIP package from a project plus image payloads.

- [x] Extend source contract and run RED for missing archive/export types.
- [x] Add xUnit tests for traversal/duplicate/oversize rejection, HTML escaping, Markdown escaping, self-contained HTML data URIs and DOCX required ZIP entries.
- [x] Implement bounded archive policy and writers without Windows dependencies.
- [x] Run all source gates.

### Task 3: Windows input tracker and step capture service

**Files:**
- Create: `src/Magic.Capture.App/Documentation/StepRecorderInputTracker.cs`
- Create: `src/Magic.Capture.App/Documentation/StepRecorderService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- `StepRecorderInputTracker.Clicked` emits bounded desktop click events and safe gestures only while started.
- `StepRecorderService.Start/Stop/StepCaptured` converts clicks to captured `DocumentationStepAsset` values through existing monitor/UIA/capture services.

- [x] Add source contract requiring `SetWindowsHookExW`, `CallNextHookEx`, safe-key filtering, disposal, and absence from resident-event startup; run RED.
- [x] Implement session-scoped hooks with duplicate-click suppression and never store printable keys.
- [x] Implement UIA-assisted bounded region capture and deterministic metadata/description generation.
- [x] Register services but do not start them from `App.OnLaunched`/resident event wiring.
- [x] Run source gates.

### Task 4: Project store and export service

**Files:**
- Create: `src/Magic.Capture.App/Documentation/DocumentationProjectStore.cs`
- Create: `src/Magic.Capture.App/Documentation/DocumentationCardRenderer.cs`
- Create: `src/Magic.Capture.App/Documentation/DocumentationExportService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Store saves/loads atomic `.magicdoc` ZIP packages.
- Card renderer turns each step asset into a bounded PNG card with numbering, text and click marker.
- Export service writes long PNG/PDF/DOCX/HTML/Markdown/offline HTML.

- [x] Add source contract and run RED for missing store/renderer/export wiring.
- [x] Implement atomic package save/load with fixed canonical entry names and bounded stream reads.
- [x] Implement card renderer with checked pixel limits and marker rendering.
- [x] Implement all six export paths using temp promotion and existing PDF writer.
- [x] Run source gates.

### Task 5: Dedicated Documentation Builder UI

**Files:**
- Create: `src/Magic.Capture.App/Views/DocumentationWindow.xaml`
- Create: `src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Window owns one in-memory project/session and calls the services above.
- MainWindow only launches the dedicated window after `AdvancedWorkflows` entitlement check.

- [x] Add source/XAML handler contract and run RED.
- [x] Implement Start/Stop, edit/add/remove/duplicate/merge/reorder, Save/Open and export controls.
- [x] Add launcher entry without expanding resident startup behavior.
- [x] Run XAML/XML/lexical/repository gates.

### Task 6: Audit, release metadata and package verification

**Files:**
- Modify: `release/feature-audit-660.json`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Create: `docs/RELEASE_NOTES_4.8.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Release is 4.8.0 / 4.8.0.0.
- Audit promotes only capabilities fully source-wired in this wave; runtime-sensitive hooks remain subject to Windows release smoke tests.

- [x] Add 4.8 audit invariants to verifier first and run RED against 4.7 metadata.
- [x] Promote the completed `3.1-step-recorder-docs` rows conservatively and regenerate human-readable audit.
- [x] Synchronize app/MSIX/source version and write release notes/checklist.
- [x] Run `verify-repo.py`, `verify-structure.py`, `verify-csharp-lexical.py`.
- [x] Run `source-release.py`, extract the exact ZIP, and rerun all static gates from packaged source.
