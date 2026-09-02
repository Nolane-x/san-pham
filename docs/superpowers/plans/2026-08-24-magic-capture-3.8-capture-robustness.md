# Magic Capture 3.8 Capture Robustness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add horizontal and bounded 2D scrolling capture plus physical-pixel/DPI topology validation and bounded GDI capture recovery.

**Architecture:** Keep pure geometry/overlap/grid planning in `Magic.Capture.Core`, Windows input/DPI/GDI integration in `Magic.Capture.App`, and keep existing vertical capture source-compatible. Add horizontal stitching as a sibling of the vertical stitcher and compose rows for 2D capture rather than building an unrelated image engine.

**Tech Stack:** .NET 10, C# 14, WinUI 3, Win32 P/Invoke, System.Drawing, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-3.8-capture-robustness-design.md`

## Global Constraints

- Preserve local-first/offline behavior and existing entitlement model.
- Do not mark Windows Graphics Capture or Desktop Duplication Done.
- Maximum automatic scroll frames: 64.
- Maximum 2D grid: 8×8 / 64 tiles.
- Keep all capture coordinates in physical desktop pixels.
- Keep all retry loops bounded.
- Existing image workload limits remain authoritative.
- Windows build/xUnit/MSIX remain external gates when the toolchain is unavailable.

---

### Task 1: Core scroll-axis and grid planning

**Files:**
- Create: `src/Magic.Capture.Core/Capture/ScrollCapturePlan.cs`
- Test: `tests/Magic.Capture.Core.Tests/ScrollCapturePlanTests.cs`

**Interfaces:**
- Produces: `ScrollAxis`, `ScrollVector`, `ScrollCaptureGridPlan`, `ScrollCaptureTile`.

- [x] Write tests for row-major tile order, horizontal/vertical vectors, 8×8 maximum, and invalid dimensions.
- [x] Verify the source contract is red because the production types do not yet exist.
- [x] Implement immutable plan types and checked bounded planning.
- [x] Verify lexical/structural gates and source contract are green.

### Task 2: Horizontal overlap matching

**Files:**
- Create: `src/Magic.Capture.Core/Imaging/HorizontalOverlapMatcher.cs`
- Test: `tests/Magic.Capture.Core.Tests/HorizontalOverlapMatcherTests.cs`

**Interfaces:**
- Produces: `HorizontalOverlapMatch`, `HorizontalOverlapOptions`, `HorizontalOverlapMatcher.Find`.

- [x] Write exact-overlap and rejection tests first.
- [x] Verify missing production symbol contract is red.
- [x] Implement column-overlap matching with bounded row/column sampling.
- [x] Run static gates.

### Task 3: Horizontal stitcher

**Files:**
- Create: `src/Magic.Capture.App/Imaging/HorizontalImageStitcher.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Produces: `HorizontalImageStitcher.Stitch(IReadOnlyList<byte[]>)` and `HorizontalStitchResult`.

- [x] Add source contracts for output limits and horizontal matcher usage.
- [x] Implement same-height validation, overlap discovery, checked output width, and bounded bitmap allocation.
- [x] Register service.
- [x] Run static gates.

### Task 4: Horizontal wheel input and direction-aware automatic capture

**Files:**
- Modify: `src/Magic.Capture.App/Platform/Native/NativeConstants.cs`
- Modify: `src/Magic.Capture.App/Platform/InputSynthesisService.cs`
- Modify: `src/Magic.Capture.App/Capture/AutomaticScrollCaptureService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Consumes: `ScrollAxis`, `HorizontalImageStitcher`.
- Produces: `ScrollHorizontal`, axis-aware options/results.

- [x] Add source tests/contracts for `MOUSEEVENTF_HWHEEL` and axis dispatch.
- [x] Implement horizontal input synthesis.
- [x] Generalize automatic capture without changing vertical default behavior.
- [x] Add horizontal overlap analysis and horizontal stitch dispatch.
- [x] Run static gates.

### Task 5: Bounded 2D scrolling capture

**Files:**
- Create: `src/Magic.Capture.App/Capture/TwoDimensionalScrollCaptureService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Consumes: `ScrollCaptureGridPlan`, `ScreenCaptureService`, `HorizontalImageStitcher`, `VerticalImageStitcher`, `InputSynthesisService`.
- Produces: `TwoDimensionalScrollCaptureResult` and bounded row-major capture.

- [x] Add source contract requiring max 64 tiles and row-major scroll reset.
- [x] Implement tile capture with cursor restore and bounded settle delays.
- [x] Measure seam overlaps across the grid, use median boundary overlaps, and compose one checked 2D canvas.
- [x] Reject wrong-sized/near-duplicate tiles with explicit errors.
- [x] Run static gates.

### Task 6: DPI and physical desktop topology

**Files:**
- Create: `src/Magic.Capture.Core/Capture/DesktopPixelTopology.cs`
- Test: `tests/Magic.Capture.Core.Tests/DesktopPixelTopologyTests.cs`
- Modify: `src/Magic.Capture.App/Capture/MonitorInfo.cs`
- Modify: `src/Magic.Capture.App/Capture/MonitorService.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeMethods.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeConstants.cs`

**Interfaces:**
- Produces: `DesktopPixelMonitor`, `DesktopPixelTopology`, monitor DPI/scale metadata.

- [x] Write tests for negative coordinates, portrait monitors, clipping, local/desktop round-trip, invalid DPI.
- [x] Add `GetDpiForMonitor` P/Invoke with safe fallback to 96 DPI.
- [x] Extend monitor metadata and topology snapshot.
- [x] Run static gates.

### Task 7: Bounded GDI retry diagnostics

**Files:**
- Create: `src/Magic.Capture.App/Capture/CaptureAttemptDiagnostics.cs`
- Modify: `src/Magic.Capture.App/Capture/ScreenCaptureService.cs`

**Interfaces:**
- Produces: max-three-attempt GDI capture behavior and structured diagnostics.

- [x] Add source contract for bounded retries and no unbounded loops.
- [x] Implement max-three attempt policy for transient screen-copy failures.
- [x] Validate encoded dimensions before returning a `CaptureAsset`.
- [x] Run static gates.

### Task 8: Capture mode UI

**Files:**
- Create: `src/Magic.Capture.App/Views/ScrollingCaptureModeDialog.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Produces: Vertical/Horizontal/2D Grid choice; 2–8 row/column bounds.

- [x] Add structural contract for three modes and bounded row/column inputs.
- [x] Implement compact ContentDialog.
- [x] Route vertical/horizontal to automatic service and grid to 2D service.
- [x] Preserve status/error handling and entitlement checks.
- [x] Run static gates.

### Task 9: Audit, verifier, version, docs, release

**Files:**
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `scripts/verify_repository.py`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Create: `docs/RELEASE_NOTES_3.8.0.md`

**Interfaces:**
- Produces: source-truth ledger and deterministic source release.

- [x] Promote only implemented audit rows (#2, #3, #33, #40, #41, #42).
- [x] Add verifier contracts for 3.8 architecture and keep #31/#32 Foundation.
- [x] Bump version to 3.8.0 / 3.8.0.0.
- [x] Run repository, structural, lexical, XML, audit, and ZIP integrity gates.
- [x] Extract packaged ZIP to a clean directory and rerun gates on the bundle.
- [x] Write SHA-256 sidecar.
