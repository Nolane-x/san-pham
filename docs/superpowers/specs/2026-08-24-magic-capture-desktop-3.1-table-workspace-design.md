# Magic Capture Desktop 3.1 Table Workspace Design

## Goal

Turn a detected table into a bounded local editing workspace without adding a database, cloud service, resident worker or large third-party spreadsheet dependency.

## Scope

This wave targets #517, #519, #520, #521, #522 and #527 when end-to-end. #518 (moving source-image row/column extraction separators), #506/#509 (automatic merged/span detection) and #523 (formula inference) remain separate because they require different extraction semantics.

## Core document model

`EditableTableDocument` owns rectangular cell text plus non-overlapping merge ranges. It is bounded to 2,048 rows, 128 columns, 100,000 cells, 4,096 characters per cell and 2,000,000 total characters. Source `DetectedTable` is copied into the document; edits never mutate OCR/table analysis objects.

Pure Core operations provide set-cell, insert/delete row, insert/delete column, merge/unmerge, rectangular selection normalization and TSV copy. Structural edits transform merge ranges deterministically. Merge operations preserve underlying cell values in memory so Unmerge restores the pre-merge content; rendering/export hides follower-cell values while a merge is active.

## Workspace UX

`TableWorkspaceWindow` is Plus-gated through the existing Table result path. It renders a paged grid instead of materializing an entire spreadsheet: at most 64 rows × 16 columns (1,024 cell buttons) exist in XAML at once. Row and column paging controls navigate larger tables.

A single editor TextBox edits the active cell. Selection is one cell by default; an explicit `Extend selection` toggle makes the next clicked cell define the opposite corner, avoiding dependency on uncertain platform modifier-key behavior. Toolbar actions insert/delete rows/columns, merge/unmerge, copy selection, export XLSX and compare with a local CSV/TSV file.

## XLSX

`TableXlsxWriter` emits a minimal standards-compatible XLSX using `System.IO.Compression.ZipArchive` and inline-string cells. OCR/user text is never emitted as formulas. Merge regions are written to `<mergeCells>`. The writer validates document limits before allocation and uses fixed ZIP entry timestamps for deterministic output.

## Table compare

`DelimitedTableParser` reads bounded CSV/TSV text using a quote-aware state machine. File size is checked before `FileIO.ReadTextAsync`. `TableDiffEngine` compares dimensions and cells and returns at most 1,000 changes plus a truncation flag. The workspace renders only a bounded textual diff summary.

## Performance / safety

- No background service or polling.
- At most 1,024 cell controls in the workspace page.
- No arbitrary formula execution; XLSX stores all cells as inline strings.
- Local compare file <= 2 MB before allocation.
- Document and parser enforce row/column/cell/text limits before materialization where possible.
- Copy/XLSX/diff output is bounded and deterministic.

## Verification

Core tests cover edit operations, merge-range transforms, TSV selection, CSV quoting, diff bounds and XLSX ZIP/XML structure. Repository contracts verify paging bounds, explicit selection UX, file-size preflight and Result-window wiring. Real Excel/LibreOffice opening, WinUI grid interaction and x64/ARM64 build remain Windows release gates.
