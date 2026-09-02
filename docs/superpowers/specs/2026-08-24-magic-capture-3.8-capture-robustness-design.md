# Magic Capture Desktop 3.8 — Capture Robustness and 2D Scrolling Design

## Goal

Make the capture engine materially stronger on real multi-monitor Windows desktops without pretending unsupported GPU paths are complete. The release adds horizontal scrolling capture, bounded 2D scrolling capture, a physical-pixel/DPI topology model, and retry/diagnostic protection around the existing GDI capture path.

## Scope

This wave implements four connected capabilities:

1. **Generalized scroll axis** — vertical and horizontal capture share one direction model, one bounded correction policy, and axis-specific overlap matching.
2. **2D scrolling capture** — deterministic row-major tiling over a bounded grid using horizontal and vertical scroll synthesis, with per-tile validation and a final two-dimensional stitch.
3. **Mixed-DPI coordinate robustness** — monitor topology includes effective DPI/scale metadata and validates that all capture rectangles remain in physical desktop pixels, including negative monitor coordinates and portrait displays.
4. **GDI capture reliability** — retry transient `CopyFromScreen` failures, verify output dimensions, expose structured capture-attempt diagnostics, and fail closed after bounded retries.

Recording/video, Windows Graphics Capture, and Desktop Duplication remain separate subsystems. Their audit status must not be promoted merely because this release improves GDI behavior.

## Architecture

### 1. Core axis primitives

Create `ScrollAxis`, `ScrollVector`, and `ScrollCaptureGridPlan` in `Magic.Capture.Core.Capture`. The plan converts a requested 2D scroll grid into a deterministic sequence of tile coordinates and scroll vectors. It enforces hard limits on rows, columns, and total frames.

Create `HorizontalOverlapMatcher` beside `VerticalOverlapMatcher`. It operates on grayscale buffers and returns `HorizontalOverlapMatch(OverlapColumns, MeanAbsoluteDifference)`. Vertical behavior remains source-compatible.

### 2. Generalized input synthesis

`InputSynthesisService` gains `ScrollHorizontal(int wheelDelta)` using `MOUSEEVENTF_HWHEEL` and `Scroll(ScrollVector)` for axis-neutral callers. Existing `ScrollVertical` remains available.

### 3. Horizontal capture

`AutomaticScrollCaptureService` becomes direction-aware through `AutomaticScrollCaptureOptions.Axis`. Vertical remains the default. Horizontal mode:

- synthesizes horizontal wheel input;
- detects end-of-content using the same sampled frame difference;
- applies bounded reverse-wheel alignment correction;
- finds left/right overlap with `HorizontalOverlapMatcher`;
- stitches with `HorizontalImageStitcher`;
- reports axis in progress/result metadata.

Sticky top/bottom detection remains vertical-only in 3.8. Horizontal mode must not incorrectly trim top/bottom chrome as left/right chrome.

### 4. 2D scrolling capture

Add `TwoDimensionalScrollCaptureService`. It consumes a rectangular capture region and a bounded `ScrollCaptureGridPlan` (default 2×2, maximum 8×8 / 64 frames). It captures row-major tiles:

- capture `(row, column)`;
- horizontal scroll between columns;
- reverse horizontal scroll to the row origin before moving to the next row;
- vertical scroll between rows;
- reject near-duplicate motion that indicates the target did not scroll;
- collect only same-dimension tiles;
- measure horizontal seams across every row and vertical seams across every column, use the median overlap for each grid boundary, then place all tiles on one checked physical-pixel canvas.

This is intentionally a bounded 2D capture mode, not an unbounded web crawler. The user chooses the region, row count, and column count; safety limits prevent runaway input or huge bitmap allocation.

### 5. DPI/monitor topology

Extend `MonitorInfo` with `DpiX`, `DpiY`, `ScaleX`, and `ScaleY`. `MonitorService` queries effective DPI with `GetDpiForMonitor` when available and falls back to 96 DPI. It also exposes `DesktopTopologySnapshot` containing virtual bounds and monitors.

Add pure Core `DesktopPixelTopology` validation that verifies:

- every monitor has positive dimensions;
- monitor rectangles may have negative X/Y;
- DPI must be finite and positive;
- a requested capture rectangle is clipped only in physical-pixel desktop coordinates;
- local/desktop conversion round-trips for portrait and negative-coordinate monitors.

The overlay continues to scale pointer DIPs to physical monitor pixels using actual rendered size; the new model documents and tests the invariant instead of mixing logical and physical coordinates.

### 6. GDI reliability and diagnostics

`ScreenCaptureService` gains bounded retry options and `CaptureAttemptDiagnostics`. A capture attempts `CopyFromScreen` up to three times with short delays only for transient `ExternalException`/`Win32Exception` failures. Every successful result is dimension-validated before encoding.

No fallback path may silently return a blank/zero-sized image. If GDI fails after the retry budget, the exception contains attempt count and rectangle metadata.

### 7. UI

The existing automatic scrolling action remains the entry point. After rectangular region selection, show a compact mode dialog:

- Vertical (default)
- Horizontal
- 2D Grid

2D exposes rows and columns (2–8). The dialog does not expose low-level wheel deltas by default. Status messages show axis/grid progress and alignment corrections.

### 8. Testing and release truth

Core tests cover horizontal overlap, grid-plan ordering/limits, topology conversion, negative coordinates, portrait monitors, and DPI validation. App-level source contracts verify horizontal wheel synthesis, mode wiring, bounded capture retries, and 2D service registration.

Because this environment lacks Windows/.NET execution, xUnit/WinUI/MSIX runtime results must remain an external gate. Static verification may prove structure and source contracts only.

## Audit truth

Promote only capabilities with concrete implementation:

- #2 Horizontal Scrolling Capture → Done
- #3 2D scrolling capture → Done
- #33 Legacy GDI fallback when needed → Done only if bounded retry/diagnostics are implemented as the current capture fallback path
- #40 Mixed-DPI multi-monitor alignment → Done after topology/DPI invariants are implemented
- #41 Negative monitor coordinate support → Done after dedicated topology tests/contracts
- #42 Portrait-monitor edge cases → Done after dedicated topology tests/contracts

Keep #31 Windows Graphics Capture GPU path and #32 Desktop Duplication fallback at Foundation. Keep hardware torture tests in ReleaseTest.

## Safety limits

- Maximum 64 frames for any automatic scroll session.
- Maximum 8 rows × 8 columns for 2D capture.
- Existing image workload pixel/dimension limits remain authoritative.
- Scroll correction retries remain bounded to 3.
- GDI capture attempts remain bounded to 3.
- All arithmetic that can expand dimensions uses checked operations and existing `ImageWorkloadLimits`.
