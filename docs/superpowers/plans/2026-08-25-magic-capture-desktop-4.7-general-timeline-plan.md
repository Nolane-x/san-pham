# Magic Capture Desktop 4.7 General Timeline & Keyframes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a shared easing/keyframe/audio-envelope architecture and usable Clip Editor controls while preserving the 4.6 fast path.

**Architecture:** Core owns deterministic timeline semantics. App renderers consume Core records. UI edits records through existing Undo/Redo.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Editing, System.Drawing overlay raster cache, xUnit Core tests.

**Spec:** `docs/superpowers/specs/2026-08-25-magic-capture-desktop-4.7-general-timeline-design.md`

## Global Constraints
- No cloud/backend dependency.
- Existing 4.6 capture/recording runtime remains unchanged.
- Overlay generated pieces <= 2048.
- Overlay cache <= 256 files / 64 MiB.
- Audio-envelope keyframes <= 128 per segment.
- Future project schemas stay read-only.

---

### Task 1: Core schema, easing and envelope models
- [x] Add failing source tests for v4/easing/overlay/audio/text semantics.
- [x] Add schema v4 and migration-compatible optional record fields.
- [x] Add common easing, rich text style, overlay animation and audio envelope policy.
- [x] Extend frame-effect interpolation with easing.

### Task 2: Render integration
- [x] Render animated overlays as bounded native overlay pieces.
- [x] Include rich style in content-addressed text raster assets.
- [x] Apply audio envelope gain using output-local segment time in advanced PCM retiming.

### Task 3: Clip Editor UI
- [x] Add rich title/text controls.
- [x] Add overlay and frame-effect keyframe editors.
- [x] Add audio envelope manual keyframes and fade/duck presets.
- [x] Keep all edits inside existing Undo/Redo project history.

### Task 4: Release hardening
- [ ] Update repository contracts for historical schemas plus v4.
- [ ] Run repository/structure/lexical gates.
- [ ] Bump source/MSIX to 4.7.0 / 4.7.0.0 without changing audit status counts.
- [ ] Create deterministic source ZIP, re-extract, rerun all gates, and publish SHA-256.
