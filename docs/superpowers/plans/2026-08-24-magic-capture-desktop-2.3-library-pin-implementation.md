# Magic Capture Desktop 2.3 Library + Pin Power UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close additional high-value 660-backlog items through History filters/batch workflows, capture sessions, Pin 2.0 and capture size presets without new idle services.

**Architecture:** Add deterministic History query/session primitives in Core, then wire thin WinUI controls. Pin actions reuse existing Clipboard/Export/Editor services and keep image operations on demand.

**Tech Stack:** .NET 10, C#, WinUI 3, existing JSON History store, System.Drawing image normalization.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-2.3-library-pin-design.md`

## Global Constraints

- No new resident polling/background worker.
- No new database/cloud dependency.
- All user metadata stays normalized/bounded.
- Batch operations are selection-scoped and collision-safe.
- Windows compile/xUnit/XAML remains a mandatory release gate unavailable in this Linux environment.

---

### Task 1: History Query 2.0 Core

**Files:**
- Create: `src/Magic.Capture.Core/History/HistoryQuery.cs`
- Create: `tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs`

- [ ] Write filtering tests for date/source/window/type/dimensions/OCR/barcode/favorite/session.
- [ ] Write sorting tests for newest/oldest/file-size.
- [ ] Implement a pure bounded query engine.

### Task 2: Capture session metadata

**Files:**
- Modify: `src/Magic.Capture.App/Persistence/HistoryStore.cs`
- Modify: `src/Magic.Capture.Core/History/HistoryItem.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistoryMetadataTests.cs`

- [ ] Normalize bounded session IDs.
- [ ] Assign one stable session ID to new captures for the lifetime of the resident app process.

### Task 3: History filter UX

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

- [ ] Add compact Filters dialog and sort selector.
- [ ] Wire filter state through `HistoryQuery.Apply`.
- [ ] Surface active-filter count and session metadata without cluttering the list.

### Task 4: History batch operations + import

**Files:**
- Modify: `src/Magic.Capture.App/Persistence/HistoryStore.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

- [ ] Add bounded batch delete and batch metadata update primitives.
- [ ] Add batch tag UX.
- [ ] Add collision-safe batch export to a user-selected local folder.
- [ ] Add multi-image import with PNG normalization and per-file failure reporting.

### Task 5: Pin 2.0

**Files:**
- Modify: `src/Magic.Capture.App/Views/PinWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/PinWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

- [ ] Add zoom out/in, fit, actual-size and reset commands.
- [ ] Add Copy, Save and Edit commands through existing services.
- [ ] Make click-through a reversible toggle.
- [ ] Persist selected opacity through normalized application settings.

### Task 6: Capture size presets

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Test: existing capture geometry tests where applicable.

- [ ] Add named 720p/1080p/1440p/4K/square/social presets to Exact Region dialog.
- [ ] Keep custom X/Y/W/H entry and virtual-desktop clipping.

### Task 7: Ledger/version/docs/release

**Files:**
- Modify: `release/feature-audit-660.json`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `release/version.json`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/COMPREHENSIVE_UPGRADE_ROADMAP.md`
- Create: `docs/RELEASE_NOTES_2.3.0.md`

- [ ] Reclassify only genuinely end-to-end items to Done.
- [ ] Synchronize 2.3.0 metadata.
- [ ] Run verifier + structural checks.
- [ ] Create deterministic source ZIP + SHA-256 and verify integrity.
