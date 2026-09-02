# Magic Capture Desktop 4.5 Advanced Clip Editor Design

## Scope

4.5 extends the isolated Clip Editor introduced in 4.4. It does not alter screenshot capture or the live recorder pipeline. The wave adds title cards, timed text/arrow/shape overlays, timed redaction, automatic rectangular redaction tracking, audio extraction, and local video format conversion.

## Architecture

`Magic.Capture.Core.VideoEditing` owns schema-v2 models, validation, migration, normalized overlay geometry, bounded tracker math, and output-format policy. App services own Windows Media Editing/Transcoding integration. `MediaComposition` remains the base timeline compositor; `MediaOverlayLayer` renders static and keyframed overlays. A bounded thumbnail tracker samples the composition without overlays and updates redaction keyframes using deterministic template matching.

## Project schema v2

Schema v2 adds:

- optional title-card timeline segments;
- timed overlay definitions;
- optional tracking keyframes;
- explicit overlay colors/opacity/normalized bounds.

Schema v1 projects are upgraded in memory to v2 and remain editable. Schema v3+ projects remain read-only. Saving never overwrites a future-schema file.

## Title cards

A title card is a real timeline segment and therefore increases composition duration. It contains duration, background ARGB, foreground ARGB, text, and normalized font scale. The base card is created with `MediaClip.CreateFromColor`; text is rendered as a transparent raster overlay for the title-card interval.

## Timed overlays

Supported overlay kinds:

- Text
- Rectangle
- Ellipse
- Arrow
- Redaction

Every overlay has a timeline start, duration, normalized bounds, opacity, fill/stroke colors, and bounded style/text fields. Text/arrow/shape assets are rasterized locally and cached by content hash under a bounded local cache. Redaction uses a solid `MediaClip.CreateFromColor` overlay and does not require a temporary raster asset.

`MediaOverlay.Delay` places overlays on the composition timeline. `MediaOverlay.Position` uses output pixels converted from normalized bounds. Overlay audio is always disabled.

## Automatic redaction tracking

Only redaction overlays are trackable. Tracking samples the base composition (overlays disabled) at a bounded interval and size. Core template matching searches a bounded neighborhood around the previous rectangle using sampled luma absolute error. The result is capped at 256 keyframes and 5 minutes. Failed/low-confidence tracking stops rather than jumping to an unrelated region.

Tracked redaction is rendered as a sequence of short solid-color `MediaOverlay` clips whose positions follow the generated keyframes. The render path has a hard cap on generated overlay pieces.

## Audio extraction

The editor can export its composed timeline audio to:

- WAV
- MP3
- M4A/AAC

The source is `MediaComposition.GenerateMediaStreamSource()`, so trim/order/mute/volume changes are reflected. Output is written to a hidden partial file/stream and promoted only after successful transcode and non-empty verification.

## Video conversion

The editor can convert the composed project to:

- H.264 MP4
- HEVC MP4 (runtime codec availability required)
- WMV

The operation uses `MediaTranscoder.PrepareMediaStreamSourceTranscodeAsync`; `CanTranscode=false` is surfaced cleanly. HEVC remains runtime-dependent and the UI must report codec unavailability instead of silently falling back to another codec.

## Bounds and failure policy

- Maximum overlays: 128.
- Maximum tracking keyframes per overlay: 256.
- Maximum generated render overlay pieces: 2048.
- Maximum text length: 1024 characters; title text: 512.
- Overlay rectangles remain strictly inside normalized `[0,1]` canvas.
- Overlay durations must be positive and inside project duration.
- No source/project file is modified during preview, tracking, extraction, or conversion.
- All exported media uses hidden partial output and atomic final promotion.
- Cancellation never promotes a partial file.

## UI

`VideoEditorWindow` gains compact sections for:

- add title card;
- overlay list and add/remove controls;
- overlay timing/geometry/style;
- auto-track selected redaction;
- extract audio format;
- convert video format.

Existing trim/cut/combine/crop/volume/frame/contact-sheet controls remain unchanged.

## Source-truth audit targets

Expected Done candidates after end-to-end implementation:

- #92 Extract audio → WAV/MP3/M4A
- #94 Add title card
- #95 Add text/arrow/shape overlay
- #97 Blur/redact region in video (redaction path; blur is not claimed)
- #98 Track blur region (automatic tracking is implemented for redaction regions)
- #99 Convert video formats

#89 playback speed and #96 post-record zoom remain Missing in this wave.
