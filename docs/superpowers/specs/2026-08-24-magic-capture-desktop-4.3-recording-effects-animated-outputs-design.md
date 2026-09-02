# Magic Capture Desktop 4.3 Recording Effects & Animated Outputs Design

## Goal

Extend the 4.2 recorder with deterministic local recording effects and visual-only animated recording outputs without weakening MP4/audio/webcam behavior.

## Scope

### Recording effects
- Cursor highlight rendered into output frames.
- Left/right click visualization with distinct rings plus a time-bounded ripple.
- Privacy-safe keystroke overlay: modifier chords, function/navigation keys, and shortcut combinations only; plain character typing is not retained or rendered.
- Recorded drawing mode: Ctrl+Alt + left-drag creates bounded strokes in the output; the low-level hook never swallows input.
- Live zoom/focus: Ctrl+Alt+Z toggles a bounded cursor-centered zoom with 150–300% factor.

### Input architecture
- `RecordingInputTracker` owns WH_MOUSE_LL/WH_KEYBOARD_LL hooks for a recording session only.
- Hook callbacks copy only the minimum event fields into bounded state; no unbounded event queue.
- Hooks always call `CallNextHookEx` and never block/modify user input.
- Input coordinates are converted from desktop physical pixels into recording-target-local coordinates by Core policy.
- Keyboard state retains only safe labels. Plain unmodified alphanumeric typing is discarded immediately.

### Frame pipeline
- Existing screen capture, optional webcam compositor, and MP4 encoder remain unchanged when effects are disabled.
- When any effect is enabled, decoded BGRA pixels pass through `RecordingEffectsCompositor` before the encoder.
- Effects use the shared active recording clock so paused wall time does not age ripples or key overlays.

### Animated recording outputs
- Add `RecordingOutputFormat`: MP4, GIF, APNG.
- MP4 keeps H.264/AAC behavior.
- GIF and APNG are visual-only; requesting system/microphone audio with them is rejected before recording starts.
- GIF uses a dependency-free managed GIF89a encoder with a fixed deterministic 3-3-2 global palette and bounded LZW blocks.
- APNG uses PNG signature/IHDR/acTL/fcTL/fdAT/IEND with zlib-compressed RGBA scanlines and checked CRC32.
- Animated WebP remains unimplemented in 4.3; audit item 76 becomes Partial because APNG is implemented but WebP is not.

### Output safety
- Animated formats use same-directory `.partial.gif` / `.partial.png` temporary files and promote only after a clean finalize.
- Recovery manifest schema advances to v4 and remains readable for v1-v3. Future schemas are read-only.
- Every encoder has frame-count, dimension, and per-frame byte sanity checks. No stale frame reuse on capture failure.

## UI
- Output format selector: MP4 / GIF / APNG.
- Effects: cursor highlight, click ripple, safe keys, drawing mode, live zoom, zoom factor.
- Audio controls remain visible but are disabled for GIF/APNG.
- Status text states effect hotkeys and privacy rule for keyboard overlay.

## Testing
- Core tests for event coordinate mapping, ripple lifetime, safe-key filtering, zoom crop math, stroke bounds, GIF LZW framing helpers, and APNG chunk/CRC helpers.
- Source contracts ensure hooks are session-scoped, callbacks call next hook, event state is bounded, animated formats reject audio, and recovery suffix/schema rules cover all formats.
- Windows release matrix adds global-hook lifecycle, pause/resume effect timing, 60 FPS effect load, GIF/APNG playback, and privacy checks.

## Non-goals
- Mouse-click sound.
- Raw typed-text logging.
- Input interception/blocking.
- Animated WebP encoder.
- Post-recording timeline editor.
- OCR/AI-driven zoom or background removal.
