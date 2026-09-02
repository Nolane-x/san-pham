# Magic Capture Desktop 2.8 Capture Shapes Design

## Goal

Add true Ellipse, Polygon, Freehand and Multi-region capture without changing the default rectangle fast path, adding idle work, or coupling geometry collection to image rendering.

## Constraints

- Product remains **Magic Capture Desktop**.
- Rectangle capture remains the default and must retain window snapping, loupe, resize handles, aspect ratios and keyboard nudging.
- New shape work exists only while the overlay is open; zero resident CPU/RAM after the overlay closes.
- Geometry from pointer input is untrusted: point/region counts, coordinates and degeneracy must be normalized in Core before rendering.
- Output remains a normal PNG `CaptureAsset`; downstream Copy/Save/Pin/OCR/Workflow/AI does not need shape-specific branches.
- No feature is marked Done until geometry → overlay → rendered PNG → coordinator is connected end-to-end.

## Architecture

### Core geometry contract

`CaptureSelectionGeometry` represents one normalized selection:

- `Rectangle` / `Ellipse`: one local physical-pixel `Bounds`.
- `Polygon` / `Freehand`: bounded local physical-pixel points plus derived bounds.
- `MultiRegion`: bounded local physical-pixel rectangles plus union bounds.

`CaptureSelectionGeometryRules` owns:

- maximum 2,048 raw points and 16 regions;
- clamp to source bounds;
- consecutive-point deduplication;
- minimum usable geometry;
- deterministic freehand simplification before persistence/render;
- polygon/freehand derived bounds;
- multi-region duplicate/empty rejection.

### Overlay

A compact Shape selector switches interaction mode:

- Rectangle: existing behavior unchanged.
- Ellipse: rectangle-like drag/resizing but ellipse visual/output mask.
- Polygon: click vertices; Finish closes the polygon; Backspace removes last vertex.
- Freehand: press-drag-release creates a closed region; sampling is bounded/simplified.
- Multi: each rectangle drag adds one region; Undo removes the last; Clear/Reselect resets.

Only rectangle supports window snap and fixed ratios. Ellipse supports bounding-box resize. Polygon/freehand/multi do not expose misleading rectangle resize handles.

### Image rendering

`CaptureSelectionImageRenderer` receives frozen PNG plus normalized geometry:

- Rectangle: existing `BitmapCodec.CropPng` fast path.
- Ellipse: crop bounds then preserve pixels inside ellipse and transparent pixels outside.
- Polygon/freehand: crop derived bounds and apply a `GraphicsPath` mask.
- Multi-region: allocate only the union bounds, render selected source rectangles at their original relative positions, leave gaps transparent.

All allocations pass existing image workload guards before creating canvases.

### Coordinator

`OverlaySelection` carries geometry instead of only bounds. `CaptureCoordinator` renders through the shape renderer, converts local bounds to global desktop bounds, and returns the same `CaptureAsset`/action/workflow contract as rectangle capture.

## UX / failure behavior

- Invalid/too-small shape does not close the overlay; the HUD explains what is missing.
- Polygon requires at least 3 vertices.
- Freehand requires a closed usable region after simplification.
- Multi-region supports 1–16 regions and shows the current count.
- Escape cancels the entire overlay; Reselect clears current geometry.
- Action bar appears only when current geometry is valid.
- Transparent masked output is explicit PNG; later format conversion may flatten according to existing exporter behavior.

## Verification

- Core tests for geometry validation, clamping, simplification, polygon/freehand bounds and multi-region limits.
- Static contracts for shape renderer and Coordinator integration.
- Structural XAML handler verification.
- Ledger promotion only for #8 Freehand, #9 Ellipse, #10 Polygon and #11 Multi-region after end-to-end source paths exist.
- Windows release checklist must manually test pointer/DPI/negative-coordinate behavior before binary release.
