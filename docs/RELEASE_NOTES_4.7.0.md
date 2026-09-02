# Magic Capture Desktop 4.7.0 — General Timeline, Keyframes & Audio Envelopes

4.7 deepens the non-destructive Clip Editor without changing capture/recorder runtime behavior.

## Highlights
- `.magicclip` schema v4 with in-memory migration from v1-v3 and future-schema read-only protection.
- Shared `Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, and `Hold` interpolation authority.
- Animated timed overlays with geometry + opacity keyframes, rendered through bounded native `MediaOverlay` pieces.
- Frame-effect keyframes now carry easing.
- Per-segment audio gain envelopes with manual keyframes plus Fade In/Out and Duck presets.
- Audio envelope evaluation follows rendered/output-local segment time, so it stays aligned at 0.25x-4x playback rates.
- Rich title/text styles: font family, weight, italic, underline, alignment, shadow, and outline.
- Timeline summary exposes overlay/effect/audio keyframe counts.

## Source-truth audit
No feature ID is promoted in this wave. The 660 ledger remains **379 Done / 64 Partial / 139 Foundation / 56 Missing / 22 ReleaseTest** because 4.7 deepens already-complete editor capabilities rather than claiming unrelated Missing IDs.

## Verification boundary
Repository, structural, lexical, schema/version, and packaged-source gates are required. xUnit/WinUI/Media Foundation runtime validation still requires the Windows toolchain and real media devices/codecs.
