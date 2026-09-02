# Magic Capture Desktop 4.6.0 — Editor Retiming, Frame Effects & Audio-Only Recording

Magic Capture Desktop 4.6 extends the non-destructive Clip Editor with rendered playback-rate transforms, timed zoom/pan and real BGRA blur/pixelate effects, while adding a true M4A/AAC audio-only recording path that never captures a screen frame. Existing simple projects retain the native MediaComposition fast path.

## Added

- `.magicclip` schema v3 with in-memory migration from schema v1/v2 and future-schema read-only protection.
- Per-segment rendered playback rate from 0.25× through 4× while preserving source-duration semantics for trim/cut operations.
- Output-to-base timeline mapping shared by video effects, overlays and audio retiming.
- Output frame-rate policy for 15/24/30/60 FPS advanced renders.
- Common timed frame-effect model with bounded keyframes.
- Post-production zoom/pan effect with interpolated start/end keyframes.
- Real separable Gaussian blur and bounded pixelate effects on BGRA frames.
- Advanced MP4 render path that samples the base composition by output timeline and applies frame effects deterministically.
- PCM 48 kHz stereo/16-bit audio staging and timeline retiming so speed changes keep A/V durations aligned; pitch changes with speed by design in 4.6.
- M4A/AAC audio-only recording using the existing WASAPI system-audio/microphone pipeline without creating a screen capture target or frame provider.
- Recording recovery journal schema v5 with audio-block progress while preserving legacy journal readability.
- Clip Editor controls for playback rate, output FPS and timed frame effects.
- Recorder output option for M4A audio-only; visual recording options are cleared and disabled in that mode.

## Source-truth audit

- Done: **379**
- Partial: **64**
- Foundation: **139**
- Missing: **56**
- ReleaseTest: **22**
- Total: **660**

Promoted to Done: #83 audio-only recording, #89 rendered playback speed and #96 post-record zoom. Runtime/hardware-sensitive capabilities remain at their previous statuses.

## Verification boundary

Static/source/package verification is executable in the current environment. A real Windows x64/ARM64 build, xUnit execution, XAML compilation, MediaComposition/MediaTranscoder rendering, WASAPI device behavior and long-running A/V drift tests still require the Windows release matrix.
