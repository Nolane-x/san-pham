# Magic Capture Desktop 4.7 General Timeline & Keyframes Design

## Goal
Make the Clip Editor's animation semantics coherent across frame effects, timed overlays, titles, and audio without changing the recorder runtime.

## Architecture
- `Magic.Capture.Core.VideoEditing` owns schema v4, easing, text-style normalization, overlay animation, and audio gain envelopes.
- `VideoEditCompositionService` continues to own native `MediaComposition` assembly. Animated overlays are approximated by bounded fixed-position `MediaOverlay` pieces; no unbounded per-frame overlay object creation is allowed.
- `VideoEditAdvancedRenderService` remains the timeline authority for playback-rate/frame effects and now applies audio envelopes in output-local segment time.
- `VideoEditorWindow` edits only Core records and commits through the existing project Undo/Redo stack.

## Invariants
1. Project schema v1-v3 remains readable and migrates in memory to v4; v5+ remains read-only.
2. Easing is one shared enum/function for frame-effect, overlay, and audio interpolation.
3. Overlay animation is capped at 2,048 generated pieces and 12 samples/second.
4. Audio-envelope gain is bounded to 0-200% and evaluated in rendered segment time, including playback-rate transforms.
5. Rich text assets remain content-addressed and bounded by the existing 256-file / 64 MiB cache.
6. No new background workers, cloud calls, telemetry, or resident camera/capture work.
7. Projects without playback-rate/frame effects/audio envelope keep the native composition fast path.

## UI
Clip Editor adds:
- rich title/text controls: family, weight, italic, underline, alignment, shadow, outline;
- overlay geometry/opacity/easing keyframes;
- frame-effect keyframe editing with easing;
- per-segment audio envelope keyframes plus Fade In/Out and Duck presets;
- timeline summary with segment/overlay/effect/audio keyframe counts.

## Verification
Source gates must validate schema v4, bounds/caps, animation/easing models, advanced audio application, XAML handler wiring, old-schema migration, version contract, and package integrity. Windows runtime still requires a real .NET/WinUI/Media Foundation gate.
