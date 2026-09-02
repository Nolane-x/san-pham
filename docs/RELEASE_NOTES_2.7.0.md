# Magic Capture Desktop 2.7.0 — Source Release Notes

Magic Capture Desktop 2.7.0 expands the non-destructive annotation object model rather than adding flattened drawing shortcuts. New tool outputs remain normal annotation layers and therefore participate in selection, grouping, z-order, visibility/lock, bounds/style editing, undo/redo and `.magiccapture` persistence.

## New editor tools

- Speech balloon.
- Callout.
- Numbered Step 1/2/3.
- Alphabetic Step A/B/C with deterministic AA/AB continuation.
- Roman Step I/II/III.
- Cursor stamp.
- Mouse-click stamp.
- Emoji layer.
- Magnify annotation.
- Spotlight annotation.
- Curved line.
- Curved arrow.
- Bracket.

Step labels are produced by deterministic Core helpers rather than UI-local counters/formatting rules.

## Deliberate scope boundary

Sticker, embedded-image layer, smart eraser, clone/retouch, paint brushes, polyline/polygon and cut-out remain separate backlog items. They are not counted complete merely because the renderer/editor can now host more annotation kinds.

## Feature ledger

The exact 660-feature ledger advances to **177 / 660 Done**.

## Verification boundary

Repository and structural verifiers pass in the Linux generation environment. Real .NET/xUnit/XAML/x64/ARM64 and editor rendering/project-reopen fixtures remain required on Windows.
