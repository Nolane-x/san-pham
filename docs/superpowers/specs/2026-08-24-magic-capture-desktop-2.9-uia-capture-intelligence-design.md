# Magic Capture Desktop 2.9 UI Automation Capture Intelligence Design

## Goal

Add on-demand Windows UI Automation intelligence to region capture without background polling, without putting COM calls on pointer-move, and without weakening capture responsiveness or privacy.

## Scope

This wave targets backlog capabilities #12, #13, #24 and #529–#542 where the implementation is genuinely end-to-end. A feature is promoted to Done only after overlay, capture metadata, ScreenGraph and release contracts agree.

## Architecture

1. `UiAutomationSnapshotService` runs only when a normal region overlay is about to open.
2. It snapshots capturable top-level windows before the overlay is activated, on a dedicated MTA worker.
3. Each window is traversed through the UI Automation Control View with explicit node, depth, string and time budgets. No UIA event subscription or resident polling is introduced.
4. The immutable snapshot contains physical desktop rectangles, stable runtime-derived keys, hierarchy, control type/name/AutomationId/value/state/access-key/process/window metadata and top-level z-order.
5. The overlay receives monitor-local snap targets only. Pointer move performs bounded in-memory hit testing; no COM call occurs while the overlay is active.
6. The selected capture projects matching UIA nodes into image-local coordinates and stores them on `CaptureAsset`.
7. `ScreenGraphService` merges those nodes into ScreenGraph alongside OCR/table/barcode evidence.
8. OCR correlation annotates UIA nodes with overlapping OCR word evidence so AI can connect accessibility semantics to rendered text.

## Performance and reliability constraints

- UIA is opt-in by capture path, on demand only.
- Maximum 384 accepted UIA nodes per snapshot.
- Maximum hierarchy depth 10.
- Maximum 12 top-level windows per active monitor snapshot.
- Maximum 512 characters per UIA string field; value text is capped more tightly where appropriate.
- Snapshot traversal has a soft elapsed-time budget and records truncation instead of expanding without bound.
- Element/provider failures are isolated per window/element. Fatal process exceptions are never swallowed.
- UIA is queried before overlay activation so Magic Capture Desktop cannot become the element under inspection.
- Window z-order wins before control area/depth during snap hit testing, preventing controls from obscured windows from stealing the snap target.
- Rectangle capture remains the fast default. If UIA is unavailable, capture behavior falls back to existing window snapping with no failure.

## Data model

Core owns immutable `UiAutomationSnapshotNode`, `UiAutomationSnapshot`, snap/projection rules and OCR correlation. App owns native COM acquisition only.

`CaptureAsset` carries optional projected `ScreenUiAutomationNode` entries. `WithPng` preserves them only when dimensions are unchanged; geometry-changing transforms invalidate them.

## UX

When control snapping is available, hovering a control shows a distinct snap outline and compact label such as `Button · Submit`. Rectangle mode can click the highlighted UIA control to select it exactly. Existing window snapping remains fallback and can be toggled with the same snap preference.

No accessibility tree panel or permanent inspector is added in this wave; that would add UI weight without improving the capture fast path.

## Privacy

UI Automation data remains local. It enters ScreenGraph under the same AI privacy/routing policy already applied to OCR/ScreenGraph. No provider receives UIA data unless the existing action explicitly sends ScreenGraph/context.

## Testing

Core tests cover normalization, z-order hit testing, coordinate projection, shape filtering, hierarchy, duplicate keys and OCR correlation. Repository contracts verify that native acquisition is bounded, occurs before overlay activation, and ScreenGraph consumes projected UIA nodes. Windows release checks cover Win32, WinUI, browser, elevated-window failure, mixed DPI and negative-coordinate monitors.
