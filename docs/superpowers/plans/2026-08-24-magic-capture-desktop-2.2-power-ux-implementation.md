# Magic Capture Desktop 2.2 Power UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close a substantial deterministic subset of the 660 requested capabilities with Editor Objects 2.0, Compare 2.0, local outbound redaction, and capture precision UX.

**Architecture:** Extend immutable Core models/algorithms first, then wire thin WinUI adapters. Optional work runs only on explicit user action; no new resident polling or heavy startup dependency is introduced.

**Tech Stack:** .NET 10, C#, WinUI 3, System.Drawing local imaging, Windows OCR, existing ScreenGraph pipeline.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-2.2-power-ux-design.md`

## Global Constraints

- Desktop-first and local-first.
- No mandatory cloud/account/service.
- No new always-on background worker.
- No recording/ML runtime loaded at screenshot-only startup.
- All persisted user input is normalized and bounded.
- Windows compile/xUnit/XAML remains a mandatory release gate unavailable in the current Linux environment.

---

### Task 1: Exact 660 feature audit

**Files:**
- Create: `docs/FEATURE_AUDIT_660.md`
- Create: `release/feature-audit-660.json`

- [x] Parse the original 1..660 request source and retain exact IDs/names.
- [x] Classify every item conservatively as Done / Partial / Foundation / Missing / ReleaseGate.
- [x] Include evidence paths for every non-Missing classification.
- [x] Print exact status counts in the document header.

### Task 2: Editor Objects 2.0 core

**Files:**
- Modify: `src/Magic.Capture.Core/Annotation/AnnotationModels.cs`
- Modify: `src/Magic.Capture.Core/Annotation/AnnotationDocumentEditor.cs`
- Modify: `tests/Magic.Capture.Core.Tests/AnnotationDocumentTests.cs`

**Interfaces:**
- Produces: batch selection operations, group membership, layout alignment/distribution, editable style and bounds.

- [x] Add failing tests for group/ungroup, align, equal size, distribution and multi-layer style updates.
- [x] Implement immutable batch operations with locked-layer protection and duplicate-ID normalization.
- [x] Add internal annotation copy/paste semantics with fresh IDs.

### Task 3: Editor Objects 2.0 UI

**Files:**
- Modify: `src/Magic.Capture.App/Views/AnnotationWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/Imaging/AnnotationRenderer.cs`

- [x] Switch layer list to multi-select.
- [x] Add group/layout/copy/paste controls.
- [x] Add editable X/Y/W/H and style controls for opacity, line style, fill, font, bold, italic and text alignment.
- [x] Keep preview refresh bounded and preserve selection after edits.

### Task 4: Compare 2.0 core + service

**Files:**
- Create: `src/Magic.Capture.Core/Imaging/ImageDifference.cs`
- Create: `src/Magic.Capture.Core/Imaging/TranslationAlignment.cs`
- Modify: `src/Magic.Capture.App/Imaging/ImageCompareService.cs`
- Create: `tests/Magic.Capture.Core.Tests/ImageDifferenceTests.cs`
- Create: `tests/Magic.Capture.Core.Tests/TranslationAlignmentTests.cs`

- [x] Add failing tests for threshold, alpha/transparent ignoring, per-channel statistics and bounded translation search.
- [x] Implement difference classification and alignment primitives without image allocations in Core.
- [x] Generate grayscale difference, binary mask and heatmap output in the app service.

### Task 5: Compare 2.0 UI

**Files:**
- Modify: `src/Magic.Capture.App/Views/CompareWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/CompareWindow.xaml.cs`

- [x] Add threshold slider and ignore-transparent toggle.
- [x] Add heatmap, mask, blink and triptych modes.
- [x] Add bounded auto-align translation command.
- [x] Show per-R/G/B mean difference metrics.

### Task 6: Privacy Pipeline core/settings

**Files:**
- Modify: `src/Magic.Capture.Core/Privacy/SensitiveDataDetector.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettings.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettingsRules.cs`
- Modify: `tests/Magic.Capture.Core.Tests/SensitiveDataDetectorTests.cs`
- Modify: `tests/Magic.Capture.Core.Tests/AppSettingsNormalizationTests.cs`

- [x] Add sensitive-word matching with bounded settings.
- [x] Persist redact-before-copy/save/pin/workflow and redaction style.
- [x] Normalize custom regex and word lists with strict caps.

### Task 7: Privacy Pipeline app integration

**Files:**
- Create: `src/Magic.Capture.App/Privacy/CaptureRedactionService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

- [x] Redact outbound copy/save/pin/workflow only when enabled.
- [x] Fail closed when an enabled policy cannot produce a redacted payload.
- [x] Add compact settings controls; do not add a background service.

### Task 8: Capture precision UX

**Files:**
- Modify: `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettings.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettingsRules.cs`

- [x] Display physical X/Y/W/H in HUD.
- [x] Add reset/reselect action.
- [x] Add resize-handle interaction after drag.
- [x] Add light/dark overlay preference normalized in settings.

### Task 9: Verification and 2.2 source bundle

**Files:**
- Modify: `release/version.json`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/COMPREHENSIVE_UPGRADE_ROADMAP.md`
- Create: `docs/RELEASE_NOTES_2.2.0.md`

- [x] Run repository verifier and structural checks.
- [x] Regenerate the exact 660 audit after implementation.
- [x] Update 2.2 metadata/docs without overstating Windows verification.
- [x] Build deterministic source ZIP + SHA-256 and verify archive integrity.
