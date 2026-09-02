# Magic Capture Desktop 3.1.0 — Table Workspace Source Release Notes

Magic Capture Desktop 3.1.0 turns deterministic table extraction into a bounded editable local workspace without introducing a spreadsheet framework, cloud service, background worker or new native dependency. The screenshot fast path and tray idle path remain unchanged.

## Editable Table Workspace

- Paged 64×16 projection: at most 1,024 cell buttons are materialized per page even when the document is much larger.
- One reusable active-cell editor instead of one TextBox per cell.
- Visible active/range selection borders update in place; normal cell clicks do not rebuild the grid.
- Cell edits, row/column insert/delete, manual merge/unmerge, selected-range copy and bounded 20-step Undo/Redo.
- Merge followers are hidden while merged but their underlying values remain recoverable after Unmerge.
- Editable documents are bounded to 2,048 rows, 128 columns, 100,000 cells, 4,096 characters per cell and 2,000,000 characters total.
- Cell edit is copy-on-write: only the outer row array and changed row are cloned; unchanged row views are reused. Structural row insertion/deletion no longer deep-clones the document before `Create` clones the final shape.

## Local XLSX

- Deterministic XLSX output using only `System.IO.Compression` + `XmlWriter`; no spreadsheet package dependency.
- Minimal OOXML workbook/worksheet package with deterministic ZIP entry timestamps.
- Cells are emitted as `inlineStr`, never formulas, so OCR/user text beginning with `=`, `+`, `-` or `@` is not executed by Excel.
- Manual merge ranges export as worksheet merge references; merged follower cells are not duplicated.
- XML escaping and whitespace preservation are handled by `XmlWriter`.

## Table comparison / delimited input

- Local CSV/TSV parser supports quoted delimiters, escaped doubled quotes, quoted newlines and empty cells.
- Compare input is preflighted at 2 MB before `FileIO.ReadTextAsync`.
- Diff output is deterministic and bounded to 1,000 changed cells; the UI renders at most 200 detailed rows.

## Copy/export hardening

- Selected-cell TSV copy has an explicit 2,500,000 encoded-character budget. The encoded length is checked before appending each escaped cell, so quote-heavy content cannot double output memory without a bound.
- Selected TSV preserves empty cells and hides merge followers consistently with the visible workspace.

## Exact feature ledger

3.1.0 promotes exactly six source features to `Done`:

- #517 Spreadsheet-like editing preview
- #519 Add/delete row/column
- #520 Fix cell merge manually
- #521 Copy selected cells
- #522 XLSX export
- #527 Compare two tables

#518 (manual row/column extraction separators) remains `Foundation`; automatic merged-cell/span detection (#506/#509) is also not claimed by this workspace.

Ledger after this wave: **222 Done / 92 Partial / 201 Foundation / 123 Missing / 22 ReleaseTest = 660**.

## Verification scope

The Linux generation environment can run repository, structural/XAML-handler and lexical source gates plus deterministic archive verification. It does not contain the .NET 10 / WinUI Windows toolchain, so xUnit execution, XAML compilation, Microsoft Excel interoperability, x64/ARM64 builds and MSIX runtime tests remain mandatory Windows gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
