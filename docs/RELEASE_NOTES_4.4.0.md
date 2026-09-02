# Magic Capture Desktop 4.4.0 — Recording Post-Processing / Clip Editor

Magic Capture Desktop 4.4 adds a non-destructive local clip editor on top of the 4.3 recorder. The recorder runtime remains isolated; edits are represented as a bounded project graph and rendered only when the user explicitly exports.

## Added

- Separate Clip Editor window with add/open/save project, undo/redo, reorder, duplicate and remove.
- `.magicclip` project schema v1 with a 4 MiB file cap, atomic persistence and future-schema read-only protection.
- Trim head/tail and deterministic middle-cut splitting.
- Combine/reorder clips from multiple local MP4 sources.
- Per-segment mute and 0–200% volume.
- Normalized crop and bounded even-dimension output resize.
- Windows-native MediaComposition preview and precise MP4/H.264 render.
- Same-directory hidden `.partial.mp4` render staging; final file promotion happens only after successful non-empty render.
- PNG frame capture and bounded contact-sheet export.
- Contact sheets are capped at 64 frames and 256 MiB BGRA before allocation.

## Source-truth audit

- Done: **370**
- Partial: **64**
- Foundation: **139**
- Missing: **65**
- ReleaseTest: **22**
- Total: **660**

Promoted to Done: #84, #85, #86, #87, #88, #90, #91, #93 and #100. Playback speed #89, audio extraction #92, and #94–#99 remain Missing.

## Verification boundary

The source/static/release gates can be executed in the current environment. A real Windows x64/ARM64 build, xUnit execution, XAML compilation, MediaComposition preview/render, codec behavior and hardware/device validation still require the Windows release matrix.
