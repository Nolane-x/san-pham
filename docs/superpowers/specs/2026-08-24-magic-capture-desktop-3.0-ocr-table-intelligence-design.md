# Magic Capture Desktop 3.0 OCR + Table Intelligence Design

## Goal

Turn existing local OCR geometry and deterministic table extraction into an interactive, bounded workspace without adding a cloud dependency, resident worker, large UI-element graph, or alternative OCR runtime.

## Scope

This wave targets #491, #493–#500, #508, #512–#515, #524–#526 and #528 only where the implementation is end-to-end. Automatic language detection, multi-language OCR, confidence visualization, handwriting, LaTeX, optional OCR engines, merged cells, spreadsheet editing and XLSX remain separate work.

## OCR Workspace architecture

1. Core builds a bounded `OcrSpatialIndex` from OCR word/line geometry. It stores data only; WinUI does not create one control per OCR word.
2. Preview pointer hit-testing queries the Core index for the smallest word or containing line. The UI renders at most a handful of selection rectangles and up to 256 search-highlight rectangles.
3. Word/line click-copy is an explicit mode in the Result window. Pointer interaction never re-runs OCR.
4. Search uses normalized ordinal-ignore-case matching against bounded OCR words/lines and renders all bounded matches over the original screenshot.
5. `OcrTextReconstruction` offers Plain, Layout and Code modes. Layout preserves line/paragraph separation; Code reconstructs indentation/spacing from word X geometry using a bounded inferred character width.
6. The result window exposes installed Windows OCR languages, an Auto/Profile choice, re-run recognition, and a direct Windows language-pack settings link. No language pack is downloaded by Magic Capture Desktop.

## Table Intelligence architecture

1. `TableCellInference` classifies cells deterministically as Empty/Text/Integer/Decimal/Date/Currency/Percent using bounded, culture-aware parsing.
2. `TableSchemaInference` detects a likely header row and per-column dominant types, plus deterministic anomalies when a cell disagrees with a strong column type.
3. Table serialization accepts a bounded dialect/locale policy: comma CSV, semicolon CSV, TSV, and Excel-friendly TSV; decimal formatting can be preserved, invariant, or current-culture.
4. Capture Result shows inferred header/type/anomaly summaries and updates output when format/locale changes. Original OCR/table data remains immutable.

## Performance and UX constraints

- No background OCR loop; recognition runs only on initial Result analysis or explicit Re-run OCR.
- OCR spatial index accepts at most 8,192 words and 2,048 lines.
- Search highlights at most 256 matches and stops scanning at a hard word/line budget.
- No XAML element per OCR word; only current selection + bounded match rectangles are rendered.
- Code reconstruction caps line width/output size and never emits multi-megabyte whitespace runs.
- Table inference caps inspected cells and output text using existing table dimensions plus explicit policy bounds.
- All operations remain deterministic/local.

## Testing

Core tests cover hit testing, search bounds, paragraph/code reconstruction, typed cell inference, header detection, anomaly detection and dialect/locale serialization. Repository contracts verify bounded UI overlays, language-pack link, no per-word XAML materialization and Result-window wiring. Windows manual checks validate pointer coordinates at 100/125/150/200% DPI and language-pack behavior.
