# Magic Capture Desktop 2.3 — Library + Pin Power UX Design

## Goal

Turn existing History and Pin foundations into complete day-to-day desktop workflows while keeping screenshot-only idle CPU and memory unchanged.

## Scope

1. **History Query 2.0** — compact advanced filters for date, source/app, window title, capture type, dimensions, OCR, barcode, favorite and session plus deterministic sort orders.
2. **History batch operations** — delete, tag, export and image import for the current local Library.
3. **Capture sessions** — new History items receive a bounded per-run session identifier so related captures can be filtered together.
4. **Pin 2.0** — zoom, fit/actual/reset, copy, save, open in editor, click-through toggle, opacity persistence and a compact controls layout.
5. **Capture size presets** — exact-region dialog exposes common 720p/1080p/1440p/4K and square/social presets without adding a background service.

## Constraints

- No new database, cloud sync, account or background indexer.
- History remains JSON + user-owned image files; filters run over the already loaded in-memory metadata list.
- Batch operations are bounded to selected History rows and keep cancellation/error boundaries explicit.
- Pin image decoding occurs only when the pin opens or the user requests a pin action.
- Pin controls must remain recoverable after click-through through the existing tray recovery command.
- File import converts supported local images into normalized PNG History entries; it never modifies the source file.
- Windows compilation/XAML/native behavior remains a release gate outside this Linux environment.

## Data flow

### History query

`HistoryStore.ListAsync -> HistoryDisplayItem list -> HistoryQuery.Apply(query/options) -> ListView`

The Core query is pure and allocation-bounded. UI filter state is converted to one immutable `HistoryQueryOptions` value.

### Batch operations

`HistoryList.SelectedItems -> bounded IDs/items -> HistoryStore batch primitive or explicit local file export -> RefreshHistoryAsync`

Metadata updates are normalized by the existing `HistoryMetadata` boundary.

### Pin

`CaptureAsset -> PinWindow(asset, services) -> explicit user command -> Clipboard / Export / Editor`

Zoom changes only the view transform/scroll surface. Fit and actual-size do not re-encode the image.

## Error policy

- Invalid date/dimension filters normalize to no constraint rather than throwing.
- Import rejects unreadable images individually and reports imported/failed counts.
- Batch delete/tag never silently acts on an empty selection.
- Batch export uses collision-safe filenames and does not overwrite existing files.
- Pin action errors surface in a compact status area and do not close the pin.

## Verification

- Core tests for HistoryQuery filtering/sorting and session normalization.
- Settings tests for any new persisted pin preference.
- XAML-handler structural verification.
- Repository verifier and exact 660 ledger regeneration.
- Windows release checklist for clipboard, picker, pin sizing, DPI and batch operations.
