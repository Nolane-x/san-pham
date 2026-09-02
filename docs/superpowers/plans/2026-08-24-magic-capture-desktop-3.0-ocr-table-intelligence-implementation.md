# Magic Capture Desktop 3.0 OCR + Table Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an interactive local OCR workspace and deterministic table type/dialect intelligence on top of existing OCR geometry.

**Architecture:** Pure Core spatial/reconstruction/inference policies own bounded deterministic logic. `CaptureResultWindow` is a thin projection layer with one image plus bounded overlays and explicit on-demand OCR reruns; no per-word UI object graph or background service is introduced.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Ocr, existing Magic.Capture.Core geometry/tables.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-3.0-ocr-table-intelligence-design.md`

## Global Constraints

- No cloud or new OCR dependency.
- No resident OCR/index worker.
- Maximum 8,192 indexed OCR words, 2,048 lines and 256 displayed search highlights.
- Result-window OCR rerun is explicit and cancellable by window lifetime.
- Do not promote confidence/handwriting/LaTeX/alternative-engine/XLSX features in this wave.

---

### Task 1: Core OCR spatial workspace
- [x] Add failing tests for word/line hit testing and bounded search.
- [x] Implement `OcrSpatialIndex` and search-match models.
- [x] Run all three source gates.

### Task 2: OCR layout/code reconstruction
- [x] Add failing tests for paragraph gaps and geometry-based code spacing.
- [x] Implement bounded Plain/Layout/Code reconstruction.
- [x] Run all gates.

### Task 3: Capture Result OCR UX
- [x] Add preview overlay Canvas without one UI element per OCR word.
- [x] Add word/line click-copy mode, OCR search/highlight and reconstruction mode.
- [x] Add installed-language combo, explicit OCR rerun and Windows language-pack settings link.
- [x] Run all gates.

### Task 4: Table schema/type inference
- [x] Add tests for integer/decimal/date/currency/percent and header inference.
- [x] Add bounded anomaly detection.
- [x] Wire inferred schema summary into Result.

### Task 5: Table dialect/locale output
- [x] Add tests for comma/semicolon/TSV/Excel-friendly serialization and decimal locale conversion.
- [x] Add format/locale controls and preserve immutable source table.
- [x] Run all gates.

### Task 6: Release contracts / ledger
- [x] Add 3.0 verifier contracts and Windows manual checks.
- [x] Promote only proven feature IDs.
- [x] Version to 3.0.0 only after clean gates.
- [x] Build deterministic ZIP twice and verify archive SHA/integrity.
