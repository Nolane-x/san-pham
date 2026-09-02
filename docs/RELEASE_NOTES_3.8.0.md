# Magic Capture Desktop 3.8.0 — Capture Robustness

3.8.0 strengthens the capture engine without falsely promoting unimplemented GPU backends.

## Horizontal scrolling capture

- New horizontal wheel synthesis through `MOUSEEVENTF_HWHEEL`.
- New grayscale horizontal overlap matcher and checked horizontal image stitcher.
- Existing automatic scrolling workflow now offers Vertical or Horizontal mode.
- Horizontal end detection, dynamic-content settling and bounded alignment correction reuse the same safety philosophy as vertical capture.

## Bounded 2D scrolling capture

- New 2D grid mode from 2×2 through 8×8, capped at 64 tiles.
- Deterministic row-major plan resets horizontal scroll before advancing to the next row.
- Grid stitching measures every horizontal/vertical seam, requires majority consensus, and uses median overlap per boundary so all tiles share one global canvas coordinate system.
- Tile geometry must remain identical in physical pixels.
- Near-duplicate motion is rejected when the target did not visibly scroll.
- Each tile gets bounded dynamic-content settling probes before it is accepted.
- Cursor and net scroll displacement are restored best-effort after completion or cancellation.

## Mixed-DPI / multi-monitor correctness

- Monitor metadata now includes effective DPI and scale.
- Physical desktop topology is modeled and tested independently from XAML DIPs.
- CaptureCoordinator routes local-to-desktop region conversion through the topology-aware monitor service.
- Negative monitor coordinates and portrait monitor geometry have dedicated Core tests.
- Per-monitor-v2 remains declared in `app.manifest`.

## GDI reliability

- Screen capture now has a hard three-attempt transient retry budget.
- Retry behavior lives in a deterministic Core policy.
- Successful PNG dimensions are checked against the requested physical-pixel rectangle.
- Exhausted retries surface backend, attempt and rectangle context instead of silently returning a bad capture.

This does **not** complete Windows Graphics Capture or Desktop Duplication. The legacy-GDI-fallback audit item remains Partial until a real multi-backend selection/fallback chain exists.

## Source validation note

This source release can be statically verified on non-Windows hosts. WinUI compilation, xUnit execution, MSIX packaging, high-refresh/HDR/D3D behavior and hardware mixed-DPI torture tests still require the Windows CI/release matrix.
