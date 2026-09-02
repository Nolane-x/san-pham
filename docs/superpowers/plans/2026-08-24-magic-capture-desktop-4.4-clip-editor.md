# Magic Capture Desktop 4.4 Clip Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local non-destructive clip editor with trim/cut/combine/crop/resize/volume/frame/contact-sheet workflows while keeping recording runtime untouched.

**Architecture:** Core owns a deterministic `.magicclip` edit graph and bounded edit policies. App maps the graph to Windows.Media.Editing.MediaComposition, previews/renders it locally, persists projects transactionally, and exposes a dedicated WinUI editor window.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Editing, Windows.Media.Effects, Windows.Graphics.Imaging, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-4.4-clip-editor-design.md`

## Global Constraints

- Minimum Windows version remains 10.0.19041.0.
- No FFmpeg, cloud processing, or new native binary dependency.
- Recorder 4.3 behavior and recording journal schema v4 remain unchanged.
- Source project schema v1 is current; future project schemas are read-only.
- Max 64 sources, 256 segments, 64 contact-sheet frames, and 256 MiB contact-sheet BGRA allocation.
- Source version becomes 4.4.0; MSIX version becomes 4.4.0.0.

---

### Task 1: Core edit graph and bounded policies

**Files:**
- Create: `src/Magic.Capture.Core/VideoEditing/VideoEditModels.cs`
- Create: `src/Magic.Capture.Core/VideoEditing/VideoEditPolicy.cs`
- Create: `tests/Magic.Capture.Core.Tests/VideoEditPolicyTests.cs`

**Produces:** `VideoEditProject`, `VideoEditSource`, `VideoEditSegment`, `VideoEditCrop`, `VideoEditRules`, `VideoContactSheetPlan`.

- [ ] Write failing tests for trim, cut, count bounds, volume, crop, output dimensions, schema and contact-sheet limits.
- [ ] Run the source contract and confirm production types do not exist.
- [ ] Implement the minimal deterministic Core model/policies.
- [ ] Run lexical/structural gates.

### Task 2: Project persistence and MediaComposition service

**Files:**
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditProjectStore.cs`
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditCompositionService.cs`
- Create: `src/Magic.Capture.App/VideoEditing/VideoEditThumbnailService.cs`

**Produces:** transactional `.magicclip` load/save, source probing, composition assembly, preview source, partial-safe MP4 render, PNG frame/contact sheet exports.

- [ ] Add source-contract checks before production files.
- [ ] Implement schema-safe project store.
- [ ] Build fresh MediaClip objects per segment with trim/volume/crop/output-size transform.
- [ ] Add preview and precise MP4 render with partial promotion.
- [ ] Add PNG frame/contact-sheet export with bounded BGRA canvas.
- [ ] Run repository/lexical gates.

### Task 3: Dedicated Clip Editor window

**Files:**
- Create: `src/Magic.Capture.App/Views/VideoEditorWindow.xaml`
- Create: `src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Produces:** open/add/save project, timeline reorder/duplicate/remove, trim/cut/crop/volume/mute, preview, render, frame and contact-sheet UI.

- [ ] Add XAML handler contract first and confirm structural RED.
- [ ] Add editor window and service wiring.
- [ ] Add entry point from recording card.
- [ ] Run structural/repository gates.

### Task 4: Audit, release contracts and package verification

**Files:**
- Modify: `release/feature-audit-660.json`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Create: `docs/RELEASE_NOTES_4.4.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `scripts/verify-repo.py`

**Produces:** source-truth promotion of #84/#85/#86/#87/#88/#90/#91/#93/#100 only; deterministic source bundle and SHA-256 sidecar.

- [ ] Add 4.4 verifier contract and make it fail before release metadata updates.
- [ ] Promote only implemented ledger rows.
- [ ] Set 4.4.0 / 4.4.0.0 and write release notes/checklist.
- [ ] Run full fresh gates.
- [ ] Create deterministic source ZIP.
- [ ] Re-extract ZIP and rerun all gates/invariants from the packaged tree.
