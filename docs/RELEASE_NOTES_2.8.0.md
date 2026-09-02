# Magic Capture Desktop 2.8.0 — Capture Shapes Source Release Notes

Magic Capture Desktop 2.8.0 resumes feature development on top of the 2.7.1 hardening checkpoint. This wave adds true non-rectangular capture while preserving the rectangle fast path, zero idle work, bounded geometry, and deterministic/local rendering.

## Audited feature progress

The exact 660-feature ledger advances from **177 / 660 Done** to **181 / 660 Done**.

Newly promoted:

- **#8 Freehand region capture**
- **#9 Elliptical region capture**
- **#10 Polygon region capture**
- **#11 Multi-region capture**

No adjacent feature is promoted merely because the new geometry infrastructure exists.

## Capture geometry

`CaptureSelectionGeometry` now normalizes Rectangle, Ellipse, Polygon, Freehand and Multi-region selections in physical pixels. Core rules enforce:

- at most 2,048 path samples;
- at most 16 regions;
- source-bound clamping;
- duplicate/degenerate rejection;
- non-zero polygon/freehand area;
- deterministic freehand point simplification;
- bounded separate-region pixel workload.

## Overlay UX

The region overlay now exposes Rectangle, Ellipse, Polygon, Freehand and Multi-region modes.

- Rectangle retains window snap, fixed ratios, keyboard nudge and resize handles.
- Ellipse uses the rectangle-style drag/resize interaction but exports an alpha-masked ellipse.
- Polygon collects physical-pixel vertices and supports Finish + Backspace/Undo.
- Freehand records a bounded physical-pixel path and closes it on release.
- Multi-region accepts 1–16 rectangles with Undo/Reselect.
- Automatic Scrolling explicitly opens the overlay in rectangle-only mode so shape capture cannot corrupt scrolling semantics.

## Multi-region output

Multi-region supports both forms required by the audited backlog:

1. **Canvas** — selected rectangles retain their relative positions inside one transparent union PNG.
2. **Separate images** — each selected rectangle becomes an independent `CaptureAsset`.

Separate output deliberately supports only predictable batch actions:

- Open → add all captures to History and open History once.
- Save → choose one folder, redact/encode/write each image sequentially with collision-safe filenames.
- Workflow → run the selected workflow once per region.

Single-image actions such as Copy/Pin/Edit/Text/Color/Magic require Canvas output rather than opening many windows or leaving an ambiguous clipboard state.

## Memory / correctness protections

- Non-rectangle rendering keeps the source image stream alive and renders directly from the decoded bitmap instead of cloning another full-frame source bitmap.
- Separate output decodes the frozen source once.
- Total separate-region pixel area is capped by the pixel-processing working-set budget.
- Total retained encoded region output is capped by the resident-selection byte budget.
- All output rectangles are revalidated against the frozen source before allocation/draw.
- Color sampling is blocked for Polygon/Freehand/Multi-region canvas because a concave/transparent center pixel is not a meaningful deterministic sample.

## Verification available in this environment

The Linux generation environment runs:

- `python scripts/verify-repo.py`
- `python scripts/verify-structure.py`
- `python scripts/verify-csharp-lexical.py`
- deterministic source packaging + ZIP integrity + SHA-256

Real .NET 10 / WinUI compilation, xUnit execution, MSIX packaging and Windows mixed-DPI/runtime validation remain mandatory before calling this source bundle a production Windows binary release.
