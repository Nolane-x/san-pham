# Magic Capture Desktop 2.8 Capture Shapes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use test-first development and verification-before-completion.

**Goal:** Add four true capture shape modes while preserving the existing rectangle fast path and zero idle cost.

**Architecture:** Core normalizes shape geometry, the WinUI overlay only collects it, and an App imaging renderer converts it into a normal PNG `CaptureAsset`. Downstream actions remain shape-agnostic.

**Tech Stack:** .NET 10, WinUI 3, System.Drawing.Common, xUnit, Python source verifiers.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-2.8-capture-shapes-design.md`

## Global constraints

- Max 2,048 raw points per polygon/freehand selection.
- Max 16 multi-regions.
- No background service/timer.
- Rectangle capture behavior must remain unchanged.
- No ledger promotion before end-to-end integration.

### Task 1: Core selection geometry
- Write tests for bounds, clamping, invalid shapes, point/region limits and simplification.
- Add `CaptureSelectionKind`, `CaptureSelectionGeometry`, `CaptureSelectionGeometryRules`.

### Task 2: Shape image renderer
- Add renderer contract checks before implementation.
- Rectangle delegates to existing crop path.
- Ellipse/polygon/freehand produce transparent masked PNGs.
- Multi-region renders only selected regions into union canvas.
- Enforce workload limits before bitmap allocation.

### Task 3: Overlay modes
- Add compact shape selector and shape visuals.
- Preserve existing rectangle interaction exactly.
- Implement ellipse drag, polygon vertices/finish/undo, freehand path, multi-region add/undo/clear.
- Show action bar only for valid normalized geometry.

### Task 4: Coordinator integration
- Carry geometry through `OverlaySelection`.
- Render selected geometry into PNG and create normal `CaptureAsset`.
- Keep global bounds/LastRegion safe and deterministic.

### Task 5: Verification / ledger / release snapshot
- Run all three source gates.
- Promote only #8–#11 if each is end-to-end.
- Update Windows checklist for shape/DPI tests.
- Version source to 2.8.0 only after gates pass.
- Build deterministic source ZIP and verify SHA/integrity independently.
