# Magic Capture Desktop 4.3.0 — Recording Effects & Animated Outputs

4.3 extends the local recorder without changing the default MP4 fast path.

## Recording effects

- Cursor highlight rendered into output frames.
- Distinct left/right click ripples with bounded lifetime.
- Privacy-safe shortcut overlay: plain unmodified character typing is discarded immediately and is never retained for rendering.
- Ctrl+Alt + left-drag recorded drawing with bounded stroke/state memory.
- Ctrl+Alt+Z cursor-centered live zoom with 150–300% scale.
- Session-scoped low-level input hooks always continue the Windows input chain and are disposed at session end.
- Zoom is applied before webcam PiP; PiP remains fixed while screen content zooms.

## Animated outputs

- Direct animated GIF recording using a managed GIF89a encoder, deterministic RGB 3-3-2 palette and bounded LZW sub-blocks.
- Direct APNG recording using PNG/APNG chunks, zlib frame compression and CRC32 validation data.
- MP4/H.264/AAC remains the default output and preserves the no-effect/no-webcam fast path.
- GIF/APNG are visual-only in 4.3 and reject requested system/microphone audio before recording starts.
- Animated WebP is not claimed complete; the combined WebP/APNG audit item remains Partial.

## Recovery and safety

- Recording journal schema is now v4 while schemas 1–3 remain readable/writable legacy schemas.
- Format-aware same-directory partial files: `.partial.mp4`, `.partial.gif`, `.partial.png`.
- Partial files are promoted only after their encoder completes successfully.
- Future schemas remain read-only.

## Source-truth audit

- 361 Done
- 64 Partial
- 139 Foundation
- 74 Missing
- 22 ReleaseTest
- 660 total

Source-level completion is not equivalent to Windows runtime certification. xUnit, WinUI/MSIX, low-level-hook behavior and playback interoperability remain mandatory Windows release gates.
