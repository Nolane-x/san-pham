# Magic Capture Desktop 4.5 Advanced Clip Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the 4.4 Clip Editor with schema-v2 title cards, overlays, automatic redaction tracking, audio extraction, and local format conversion.

**Architecture:** Keep `MediaComposition` as the timeline authority. Core owns deterministic models, migration, validation and tracker math; App services translate models into Windows Media overlays/transcodes. Overlay raster assets are content-addressed and bounded, while redaction uses native solid-color overlay clips.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Editing, Windows.Media.Transcoding, Windows.Graphics.Imaging/System.Drawing.Common, xUnit source tests, Python static/release verifiers.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-4.5-advanced-clip-editor-design.md`

## Global Constraints

- Windows minimum remains 10.0.19041.0.
- No cloud dependency and no FFmpeg dependency.
- Future `.magicclip` schemas remain read-only.
- Existing 4.4 segment projects migrate in memory and remain editable.
- Partial media output is never promoted on failure/cancellation.
- Runtime-only codec claims are not promoted to release-test claims without Windows evidence.

---

### Task 1: Core schema-v2 models, migration and tracker

**Files:**
- Modify: `src/Magic.Capture.Core/VideoEditing/VideoEditModels.cs`
- Modify: `src/Magic.Capture.Core/VideoEditing/VideoEditPolicy.cs`
- Create: `src/Magic.Capture.Core/VideoEditing/VideoEditTracking.cs`
- Modify: `tests/Magic.Capture.Core.Tests/VideoEditPolicyTests.cs`

**Interfaces:**
- Produces `VideoEditTitleCard`, `VideoEditOverlay`, `VideoEditOverlayKeyframe`, `VideoEditOverlayKind`, `VideoEditProjectMigration.UpgradeToCurrent`, and `VideoEditTemplateTracker.TrackNext`.

- [ ] Write failing source/xUnit contracts for schema v2 migration, title-card duration, overlay validation, keyframe caps, and synthetic moving-square tracker behavior.
- [ ] Verify contracts fail because v2 types do not exist.
- [ ] Implement v2 models/policy/migration/tracker with hard caps.
- [ ] Run repository structural/lexical checks and ensure the new source contracts are green.

### Task 2: Native title/overlay render path

**Files:**
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditOverlayAssetStore.cs`
- Modify: `src/Magic.Capture.App/VideoEditing/VideoEditCompositionService.cs`
- Modify: `src/Magic.Capture.App/VideoEditing/VideoEditProjectStore.cs`
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`

**Interfaces:**
- Consumes schema-v2 project models.
- Produces cached raster assets and `MediaOverlayLayer` entries for title text, text/shape/arrow, static redaction and keyframed redaction.

- [ ] Add failing source contracts for `MediaClip.CreateFromColor`, `MediaClip.CreateFromImageFileAsync`, `MediaOverlay.Delay`, `MediaOverlay.Position`, `AudioEnabled = false`, and v1→v2 store migration.
- [ ] Implement content-addressed bounded overlay raster cache and composition integration.
- [ ] Preserve v1 project editability through in-memory migration.
- [ ] Run structural/lexical/repository checks.

### Task 3: Automatic redaction tracking

**Files:**
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditTrackingService.cs`
- Modify: `src/Magic.Capture.App/VideoEditing/VideoEditThumbnailService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`

**Interfaces:**
- Produces `TrackRedactionAsync(VideoEditProject, string overlayId, TimeSpan sampleInterval, CancellationToken)` returning a project with bounded keyframes.

- [ ] Add failing source contracts for overlay-free composition sampling, max 256 samples, frame ownership, and Core tracker invocation.
- [ ] Implement bounded thumbnail sampling and automatic keyframe generation.
- [ ] Stop on low confidence or cancellation without mutating the caller project.
- [ ] Run full static gates.

### Task 4: Audio extraction and format conversion

**Files:**
- Create: `src/Magic.Capture.Core/VideoEditing/VideoEditExportPolicy.cs`
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditTranscodeService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Add tests: `tests/Magic.Capture.Core.Tests/VideoEditExportPolicyTests.cs`

**Interfaces:**
- `VideoEditAudioFormat`: Wav, Mp3, M4a.
- `VideoEditVideoFormat`: H264Mp4, HevcMp4, Wmv.
- `ExtractAudioAsync` and `ConvertVideoAsync` use partial-output promotion.

- [ ] Write failing format/extension/profile policy tests.
- [ ] Implement policies and MediaTranscoder service using `PrepareMediaStreamSourceTranscodeAsync`.
- [ ] Fail cleanly on `CanTranscode=false`; never silently substitute HEVC.
- [ ] Run full static gates.

### Task 5: Clip Editor UI wiring

**Files:**
- Modify: `src/Magic.Capture.App/Views/VideoEditorWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs`

**Interfaces:**
- Adds title-card, overlay list/editor, tracking, extract-audio and convert-video actions.

- [ ] Add XAML controls first and confirm structural verifier reports missing handlers.
- [ ] Implement handlers, project commits, undo/redo integration, pickers, progress/cancellation, and read-only guards.
- [ ] Ensure title-card selection does not use source-only trim/crop logic.
- [ ] Run repository/structural/lexical gates.

### Task 6: Audit, docs and deterministic release

**Files:**
- Modify: `release/version.json`
- Modify: `release/feature-audit-660.json`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Create: `docs/RELEASE_NOTES_4.5.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Modify: `scripts/verify-repo.py`
- Modify version metadata in app/MSIX projects.

**Interfaces:**
- Source-truth targets: #92/#94/#95/#97/#98/#99 Done only if all end-to-end contracts exist.

- [ ] Add 4.5 verifier contracts for schema/migration, overlays, tracking, transcodes and UI wiring.
- [ ] Update audit/version/docs without changing unrelated statuses.
- [ ] Run fresh repository/structural/lexical/release invariant gates.
- [ ] Create deterministic `Magic-Capture-Desktop-4.5.0-source.zip` and SHA-256 sidecar.
- [ ] Extract the exact ZIP to a clean tree and rerun all gates from the packaged source.
