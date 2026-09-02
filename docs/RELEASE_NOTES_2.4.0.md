# Magic Capture Desktop 2.4.0 — Source Release Notes

Magic Capture Desktop 2.4.0 strengthens the capture engine without adding resident polling, an always-on accessibility tree, or a heavyweight capture process. Window/monitor catalogs, loupe rendering and scrolling analysis are all on-demand.

## 660-feature ledger

This source snapshot reports **122 / 660 Done**. `Partial`, `Foundation`, `Missing`, and `ReleaseTest` are not counted as complete. The exact ledger is `docs/FEATURE_AUDIT_660.md` and `release/feature-audit-660.json`.

## Automatic scrolling 2.0

- conservative fixed top/bottom band detection for sticky headers and footers;
- repeated sticky chrome is trimmed from middle frames while keeping the outer header/footer;
- sampled dynamic-content settle probes with bounded retries;
- overlap is validated while capturing instead of failing only after a long session;
- bounded reverse-wheel alignment correction when a scroll step loses overlap;
- explicit dynamic/alignment/sticky progress messages;
- long capture still decodes roughly a pair of frames at a time during final stitching.

## Window and monitor targeting

- on-demand visible-window catalog, bounded at 256 entries and excluding Magic Capture Desktop itself;
- multi-select capture of up to 16 windows from one snapshot catalog;
- on-demand monitor list with primary/device/geometry information;
- selected monitor and selected windows flow through the same post-capture pipeline;
- fixed-source capture hides Magic Capture Desktop before the snapshot to avoid self-capture.

## Precision overlay

- 6× frozen-frame loupe with physical desktop coordinates;
- on-demand window snapping using the smallest containing window rectangle;
- no crop/re-encode loop while the mouse moves;
- snapping can be toggled off immediately for manual selection.

## Rich capture metadata

Window title, process name and monitor name now flow from capture asset to History metadata/query. The Library filters for source app, window title and monitor therefore have real end-to-end data for new captures.

## Verification boundary

Repository/static verification can run in this environment. Real .NET 10 / WinUI compilation, x64/ARM64 packaging, mixed-DPI Windows capture, native window enumeration, SendInput scrolling and Store validation remain mandatory release gates on Windows.
