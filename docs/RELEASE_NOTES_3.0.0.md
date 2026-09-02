# Magic Capture Desktop 3.0.0 — OCR + Table Intelligence Source Release Notes

Magic Capture Desktop 3.0.0 turns the existing local Windows OCR geometry and deterministic table extractor into a bounded interactive workspace. The release keeps the resident fast path unchanged: OCR/table work runs only when requested, no cloud dependency is added, and no background OCR/index worker is introduced.

## OCR Workspace

- Word, line and paragraph/block hit-testing directly on the captured screenshot.
- Click-to-copy for word, line or block using one selection overlay instead of one XAML control per OCR token.
- Bounded screenshot search with at most 256 rendered highlights and an explicit truncation signal (`256+`) only when an additional match exists.
- Plain, Layout and Code text reconstruction. Layout preserves paragraph gaps; Code reconstructs bounded monospace-style indentation/spacing from OCR geometry.
- Installed Windows OCR language list, Auto/Profile mode, explicit cancellable OCR rerun and a direct Windows language-pack settings link.
- Result-window OCR work is tied to window lifetime; superseded reruns are cancelled and stale results cannot overwrite newer analysis.

## Table Intelligence

- Deterministic header inference and per-column type inference for Integer, Decimal, Date, Currency and Percent values.
- Bounded type-anomaly diagnostics with row/column coordinates and expected→actual type samples.
- CSV comma, CSV semicolon, TSV, Excel-safe TSV, Markdown, HTML and JSON output.
- Preserve / invariant / current-culture numeric formatting; locale conversion applies to all data rows rather than assuming row 0 is always a header.
- Excel-safe text neutralizes formula-like OCR text while preserving genuine signed numeric values.
- Empty cells remain structurally present in delimited/JSON output.

## Hardening in this wave

- Table extraction limits OCR input before materialization: 8,192 source words, 2,048 output rows, 512 columns and 4,096 characters per reconstructed cell.
- Row-cluster center/bounds are maintained incrementally instead of repeatedly re-enumerating row words during clustering.
- Table serialization validates row/column/cell/text budgets before building output and caps generated text at 2,000,000 characters.
- Oversized output disables Copy/Save for that table view instead of pushing a giant string into WinUI or silently truncating exported data.
- Search count semantics distinguish exactly 256 matches from a genuinely truncated result set.

## Exact feature ledger

3.0.0 promotes 18 source features to `Done`:

- #491 OCR language packs UI
- #493–#496 OCR word/block selection, click-copy and screenshot search
- #498–#500 layout/code reconstruction
- #508 header detection
- #512–#516 numeric/date/currency/percent inference and empty-cell preservation
- #524–#526 CSV dialect, locale decimal and Excel-friendly output
- #528 deterministic local anomaly detection

#497 remains `Partial` because the UI intentionally renders at most 256 highlights to keep WinUI/memory bounded.

Ledger after this wave: **216 Done / 92 Partial / 207 Foundation / 123 Missing / 22 ReleaseTest = 660**.

## Verification scope

The Linux generation environment can run repository, structural/XAML-handler and lexical source gates plus deterministic archive verification. It still does not contain the .NET 10 / WinUI Windows toolchain, so xUnit execution, XAML compilation, x64/ARM64 builds, MSIX packaging and real Windows OCR/DPI runtime checks remain mandatory release gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
