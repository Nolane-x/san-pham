# Magic Capture Desktop 3.2 Image Effects 2.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the high-value local image effects and canvas operations on top of the existing bounded effect pipeline.

**Architecture:** Extend the pure Core effect contract and add a schema-bounded effect-pack serializer; keep pixel/convolution execution in the App imaging layer and separate canvas/compositing operations into one focused service. All UI entry points remain on-demand and batch paths stream one History item at a time.

**Tech Stack:** .NET 10, WinUI 3, System.Drawing/GDI+, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-3.2-image-effects-design.md`

## Global Constraints
- No cloud/network dependency.
- No new startup/background worker.
- Maximum 32 pipeline steps.
- Effect-pack payload maximum 64 KiB.
- Validate output pixel count before allocation.
- Batch processing remains sequential.

---

### Task 1: Extend Core effect model and packs
**Files:**
- Modify: `src/Magic.Capture.Core/Imaging/ImageEffectPipeline.cs`
- Create: `src/Magic.Capture.Core/Imaging/ImageEffectPackSerializer.cs`
- Test: `tests/Magic.Capture.Core.Tests/ImageEffectPipelineTests.cs`

- [ ] Write failing normalization and pack round-trip/bounds tests.
- [ ] Implement new bounded effect kinds/parameters and pack serializer.
- [ ] Run repository/static gates.

### Task 2: Pixel and neighborhood effect engine
**Files:**
- Modify: `src/Magic.Capture.App/Utilities/ImageEffectPipelineService.cs`

- [ ] Add hue, vibrance and RGB-balance transforms.
- [ ] Add sharpen, denoise, edge and mosaic using a single bounded scratch buffer.
- [ ] Preserve alpha and decode/encode once.
- [ ] Run gates.

### Task 3: Canvas/compositing operations
**Files:**
- Create: `src/Magic.Capture.App/Utilities/ImageCanvasOperationsService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

- [ ] Implement border/torn/fade/reflection/watermark/stamps.
- [ ] Implement auto-crop/expand/transparency/color-key/arbitrary rotation.
- [ ] Enforce image/payload/output budgets before allocation.
- [ ] Run gates.

### Task 4: Utilities UX and effect packs
**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

- [ ] Extend effect pipeline amount controls.
- [ ] Add import/export effect-pack actions.
- [ ] Add compact Advanced canvas effects dialog.
- [ ] Keep batch streaming sequential.
- [ ] Run XAML/structural gates.

### Task 5: Release audit
**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `release/feature-audit-660.json`
- Modify: `release/version.json`
- Modify: `README.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_3.2.0.md`

- [ ] Add 3.2 invariants to verifier.
- [ ] Promote only end-to-end features.
- [ ] Run all three source gates.
- [ ] Build deterministic ZIP twice and verify archive ledger/SHA.
