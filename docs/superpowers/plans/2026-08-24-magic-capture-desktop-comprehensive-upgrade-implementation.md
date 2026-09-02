# Magic Capture Desktop Comprehensive Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land a stability-first foundation wave that unlocks the comprehensive Magic Capture Desktop roadmap while preserving low idle cost and local-first behavior.

**Architecture:** Extend platform-neutral primitives in `Magic.Capture.Core`, wire only low-risk runtime pieces in `Magic.Capture.App`, and preserve backwards-compatible JSON contracts. Heavy capture/recording dependencies stay out of this wave and out of startup.

**Tech Stack:** .NET 10, C# records, WinUI 3, System.Drawing for existing app imaging, `System.IO.Compression` for local project packages, xUnit core tests, Python repository verifier.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-comprehensive-upgrade-design.md`

## Global Constraints

- Product identity is exactly `Magic Capture Desktop` in user-facing text.
- No mandatory cloud service or account.
- No always-on background worker for newly added features.
- Core models remain Windows-independent.
- Existing JSON must remain readable through optional/defaulted fields.
- Optional heavy tooling is not added to the app dependency graph in this wave.

---

### Task 1: Regression defects and authoritative image payload metadata

**Files:**
- Modify: `src/Magic.Capture.App/Capture/CaptureAsset.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutor.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Test: `tests/Magic.Capture.Core.Tests/CaptureImageInfoTests.cs`

**Interfaces:**
- Produces a deterministic dimension helper usable by app-side asset replacement.
- Replaces the invalid Magic Recipe `OpenEditor` call with the existing annotation entry point.

- [ ] Write failing tests for dimension normalization/validation helper.
- [ ] Run targeted test and observe feature-missing failure.
- [ ] Implement helper and wire `CaptureAsset.WithImage` to decode current image dimensions.
- [ ] Replace workflow `with { PngBytes = ... }` calls with authoritative asset replacement.
- [ ] Fix Magic Recipe editor callback to `OpenAnnotation`.
- [ ] Re-run tests/static verification.

### Task 2: Capture profiles and saved/recent regions

**Files:**
- Create: `src/Magic.Capture.Core/Capture/CaptureProfiles.cs`
- Test: `tests/Magic.Capture.Core.Tests/CaptureProfileTests.cs`

**Interfaces:**
- Produces `CaptureRegionSpec`, `CaptureProfile`, `RecentCaptureRegions` and normalization helpers.

- [ ] Write failing tests for clamping, empty-region rejection, deduplication and bounded recent regions.
- [ ] Verify red.
- [ ] Implement immutable models/helpers with no app dependency.
- [ ] Verify green.

### Task 3: Editable annotation objects

**Files:**
- Modify: `src/Magic.Capture.Core/Annotation/AnnotationModels.cs`
- Create: `src/Magic.Capture.Core/Annotation/AnnotationDocumentEditor.cs`
- Modify: `src/Magic.Capture.App/Imaging/AnnotationRenderer.cs`
- Test: `tests/Magic.Capture.Core.Tests/AnnotationDocumentTests.cs`

**Interfaces:**
- Annotation layers gain stable IDs and non-destructive object metadata.
- `AnnotationDocumentEditor` provides select/move/resize/reorder/remove/duplicate operations.

- [ ] Write failing tests for object identity, movement, resize, duplicate, z-order and locked-layer rejection.
- [ ] Verify red.
- [ ] Extend layer metadata with backwards-compatible defaults.
- [ ] Implement pure mutation helper.
- [ ] Make renderer ignore hidden layers and honor layer opacity where already representable.
- [ ] Verify green/static checks.

### Task 4: `.magiccapture` project package

**Files:**
- Create: `src/Magic.Capture.Core/Projects/MagicCaptureProject.cs`
- Create: `src/Magic.Capture.App/Persistence/MagicCaptureProjectService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Test: `tests/Magic.Capture.Core.Tests/MagicCaptureProjectTests.cs`

**Interfaces:**
- `MagicCaptureProjectManifest` schema 1 describes editable project state.
- App service atomically saves/loads ZIP packages with `manifest.json` and `base.png`.

- [ ] Write failing manifest validation tests.
- [ ] Verify red.
- [ ] Implement core manifest validator.
- [ ] Implement local ZIP package service with temp-file replace and path-safe fixed entry names.
- [ ] Register service lazily/lightweight.
- [ ] Verify tests/static checks.

### Task 5: Local sensitive-data detection and redact plan

**Files:**
- Create: `src/Magic.Capture.Core/Privacy/SensitiveDataDetector.cs`
- Create: `src/Magic.Capture.Core/Privacy/RedactionPlanner.cs`
- Test: `tests/Magic.Capture.Core.Tests/SensitiveDataDetectorTests.cs`

**Interfaces:**
- Produces `SensitiveFinding` with kind/text/bounds/confidence.
- Produces editable pixelate/blur annotation layers without changing image bytes.

- [ ] Write failing tests for email, IPv4, payment card with Luhn, JWT, private key marker, custom regex and false-positive card rejection.
- [ ] Verify red.
- [ ] Implement bounded regex scanning + Luhn validation + ScreenGraph/OCR-bound finding projection.
- [ ] Implement deterministic redaction-plan conversion.
- [ ] Verify green.

### Task 6: Workflow v2 execution policy

**Files:**
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowModels.cs`
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowValidator.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowConditionEvaluator.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutor.cs`
- Test: `tests/Magic.Capture.Core.Tests/WorkflowV2Tests.cs`

**Interfaces:**
- Optional `Condition`, `MaxAttempts`, `RetryDelayMilliseconds`, `TimeoutMilliseconds` fields on steps.
- Conditions support `exists`, `equals`, `contains`, numeric comparison and boolean composition kept deliberately small.

- [ ] Write failing evaluator/validation tests.
- [ ] Verify red.
- [ ] Implement evaluator and validator bounds.
- [ ] Wire skip/retry/timeout into executor with linked cancellation.
- [ ] Preserve schema 1 and accept schema 2.
- [ ] Verify core tests/static checks.

### Task 7: Rich history metadata and search

**Files:**
- Modify: `src/Magic.Capture.Core/History/HistoryItem.cs`
- Modify: `src/Magic.Capture.Core/History/HistorySearch.cs`
- Modify: `src/Magic.Capture.App/Persistence/HistoryStore.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistorySearchTests.cs`

**Interfaces:**
- Optional title/notes/tags/favorite/session/source/window/process fields.
- Existing history JSON remains readable.

- [ ] Add failing search tests for title/tags/notes/source metadata.
- [ ] Verify red.
- [ ] Add optional metadata fields and normalized matching.
- [ ] Add atomic metadata update method in store.
- [ ] Verify green/static checks.

### Task 8: UIA-ready ScreenGraph merge

**Files:**
- Modify: `src/Magic.Capture.Core/ScreenGraph/ScreenGraphModels.cs`
- Modify: `src/Magic.Capture.Core/ScreenGraph/ScreenGraphBuilder.cs`
- Test: `tests/Magic.Capture.Core.Tests/ScreenGraphUiAutomationTests.cs`

**Interfaces:**
- Adds `UiAutomation` node kind and `ScreenUiAutomationNode` input model.
- Builder emits stable `u#` nodes with control metadata and parent relationship when supplied.

- [ ] Write failing graph merge tests.
- [ ] Verify red.
- [ ] Add optional UIA input to build model.
- [ ] Emit attributes without changing existing OCR/barcode/signal IDs.
- [ ] Verify green.

### Task 9: Compare metric primitives

**Files:**
- Create: `src/Magic.Capture.Core/Imaging/ImageComparisonMetrics.cs`
- Test: `tests/Magic.Capture.Core.Tests/ImageComparisonMetricsTests.cs`

**Interfaces:**
- Computes MSE, PSNR and global SSIM-style luminance score over equal-size grayscale buffers.

- [ ] Write failing identical/different/invalid-input tests.
- [ ] Verify red.
- [ ] Implement numerically stable single-pass/two-pass calculations with no allocations beyond input.
- [ ] Verify green.

### Task 10: Imaging hot-path performance

**Files:**
- Create: `src/Magic.Capture.App/Imaging/PixelBufferEffects.cs`
- Modify: `src/Magic.Capture.App/Imaging/AnnotationRenderer.cs`

**Interfaces:**
- `Pixelate` and `BoxBlur` operate on locked 32bpp buffers, clipped to requested bounds.

- [ ] Preserve old renderer behavior as reference and define static invariants.
- [ ] Implement LockBits buffer operations with checked clipping.
- [ ] Replace inner `GetPixel`/`SetPixel` loops.
- [ ] Run verifier and source-level sanity checks.

### Task 11: Repository verification and release documentation

**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `docs/FEATURE_MATRIX.md`
- Create: `docs/COMPREHENSIVE_UPGRADE_ROADMAP.md`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`

**Interfaces:**
- Verifier asserts new foundation files and catches the historical invalid `Application.Current).OpenEditor(` call.
- Version advances to `2.1.0` / `2.1.0.0` for this source wave.

- [ ] Add verifier contracts for new files and source invariants.
- [ ] Update roadmap/feature matrix with implemented vs planned status.
- [ ] Advance release/app/MSIX version coherently.
- [ ] Run the full verifier and static scans.
- [ ] Package the upgraded source tree as a ZIP.
