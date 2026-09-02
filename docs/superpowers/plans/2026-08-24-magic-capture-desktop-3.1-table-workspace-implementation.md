# Magic Capture Desktop 3.1 Table Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded editable table workspace with local XLSX export and deterministic table comparison.

**Architecture:** Pure Core owns table document operations/parser/diff/XLSX. WinUI owns a paged 64×16 projection and one active cell editor; no full spreadsheet control graph is materialized.

**Tech Stack:** .NET 10, WinUI 3, System.IO.Compression, existing Magic.Capture.Core tables.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-3.1-table-workspace-design.md`

## Global Constraints

- No cloud/background worker/new spreadsheet dependency.
- At most 2,048 rows, 128 columns, 100,000 cells and 2,000,000 characters in an editable document.
- UI page renders at most 64×16 cells.
- XLSX cells are inline strings, never formulas.
- Compare input file <= 2 MB and diff output <= 1,000 changes.

---

### Task 1: Editable table Core
- [x] Write failing tests for set/insert/delete/merge/unmerge and selection TSV.
- [x] Implement bounded `EditableTableDocument`, `TableCellRange` and operations.
- [x] Run three source gates.

### Task 2: CSV/TSV parser + diff
- [x] Write failing tests for quoted CSV, empty cells, malformed bounds and diff truncation.
- [x] Implement bounded parser and deterministic diff engine.
- [x] Run gates.

### Task 3: XLSX writer
- [x] Write tests that inspect required XLSX ZIP parts, cell XML escaping and merge refs.
- [x] Implement deterministic local XLSX writer.
- [x] Run gates.

### Task 4: WinUI workspace
- [x] Add paged 64×16 table grid, active-cell editor, explicit extend-selection and edit toolbar.
- [x] Add copy selection, XLSX export and compare-file UX.
- [x] Wire Result Table tab to open workspace.
- [x] Run gates.

### Task 5: Release contracts / ledger
- [x] Add 3.1 source contracts and Windows manual tests.
- [x] Promote only proven IDs.
- [ ] Version after all gates are clean; deterministic ZIP twice with matching SHA.
