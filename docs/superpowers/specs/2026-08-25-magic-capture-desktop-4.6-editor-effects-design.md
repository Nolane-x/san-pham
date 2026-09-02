# Magic Capture Desktop 4.6 Editor Effects Design

## Goal
Complete the remaining core post-production gaps without destabilizing the 4.5 native MediaComposition fast path: rendered playback speed, post-production zoom/pan and real pixel effects, plus audio-only M4A recording.

## Architecture
Projects move to `.magicclip` schema v3. Existing `VideoEditSegment.Duration` remains source-range duration so trim/cut semantics stay backward-compatible; `RenderedDuration` is derived from `PlaybackRate` for the output timeline. A general keyframe/value model is used by zoom/pan and frame effects. Projects without speed/frame effects continue to use `MediaComposition.RenderToFileAsync`; projects requiring retiming/effects use an advanced frame renderer.

Audio-only recording is a separate output mode in the existing recording lifecycle. It reuses the 4.1 WASAPI pipeline, pause/resume clock, stop policy, recovery journal, and AAC encoder but never requests a screen frame.

## Safety and bounds
- Playback rate: 0.25x–4.0x.
- Output FPS: 15, 24, 30, or 60.
- Frame-effect keyframes: max 256 per effect, max 128 effects/project.
- Gaussian blur radius: 1–32 px; pixelate cell: 2–64 px; zoom: 1.0–4.0x.
- Advanced frame rendering hard cap: 4 hours and 500,000 output frames.
- Recording journal schema v5 remains backward-readable and future-schema read-only.
- M4A audio-only requires at least one requested audio source and forbids webcam/cursor/visual effects.

## Verification boundary
The Linux/container environment can run source, XML, lexical and repository contracts but cannot run WinUI, MediaFoundation, WASAPI or xUnit without the Windows/.NET toolchain. Windows runtime/build gates remain explicit release prerequisites.
