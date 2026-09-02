# Magic Capture Desktop 4.6 Editor Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add rendered playback speed, post-record zoom/pan and bounded real frame effects, plus audio-only M4A recording.

**Architecture:** Preserve the 4.5 MediaComposition fast path. Add schema-v3 timeline/effect primitives in Core, an advanced render service only for projects that need retiming/effects, and an audio-only branch in the existing recording lifecycle.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Editing, Windows.Media.Core/Transcoding, WASAPI via NAudio.Wasapi 3.0.1.

**Spec:** `docs/superpowers/specs/2026-08-25-magic-capture-desktop-4.6-editor-effects-design.md`

## Global Constraints
- No cloud dependency.
- Keep 4.5 native render path for simple projects.
- No unbounded pixel/audio buffers.
- Future project/journal schemas remain read-only.
- Windows runtime/build verification must not be claimed from static gates.

---

### Task 1: Schema v3, retiming and frame-effect Core
- [ ] Add failing Core tests for playback-rate normalization, rendered duration, output→source timeline mapping, keyframe interpolation, zoom, Gaussian blur, pixelate and schema migration.
- [ ] Add schema-v3 models and deterministic bounded BGRA effects.
- [ ] Run lexical/structural source gates.

### Task 2: Advanced video render path
- [ ] Add source contracts for native-fast-path selection and bounded advanced render selection.
- [ ] Add advanced frame sampler/renderer with output FPS, timeline mapping and effect application.
- [ ] Integrate with Clip Editor MP4 export while preserving existing fast path.

### Task 3: Speed-aware audio
- [ ] Add tests for PCM sample-count retiming.
- [ ] Stage composed audio as canonical PCM16 48 kHz stereo and retime by segment playback rate.
- [ ] Feed retimed audio and rendered video into the same MediaStreamSource.

### Task 4: Audio-only M4A recording
- [ ] Add tests/source contracts for M4A compatibility and journal schema v5.
- [ ] Add M4A audio encoder and session branch that never requests visual frames.
- [ ] Wire output format UI so visual controls are disabled and target selection is skipped.

### Task 5: Clip Editor UI and project integration
- [ ] Add playback-rate, output-FPS, zoom/pan, blur/pixelate and keyframe controls.
- [ ] Keep title/overlay/project undo-redo behavior.
- [ ] Add source contracts for every XAML handler.

### Task 6: Audit, release and package verification
- [ ] Promote only implemented feature IDs (#83, #89, #96) to Done.
- [ ] Bump source/MSIX to 4.6.0/4.6.0.0 and add release notes/checklist entries.
- [ ] Run repository/structural/lexical gates.
- [ ] Create deterministic source ZIP, extract it cleanly, rerun all gates, validate audit/version/schema/capabilities, and write SHA-256 sidecar.
