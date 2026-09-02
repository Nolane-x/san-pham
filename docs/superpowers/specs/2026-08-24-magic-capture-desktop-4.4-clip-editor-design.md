# Magic Capture Desktop 4.4 Clip Editor Design

## Goal

Add a non-destructive, local-only clip editor on top of Windows.Media.Editing without changing the proven 4.3 recording runtime. The editor must support trim, middle cuts, multi-clip composition/reorder, crop, output resize, per-segment mute/volume, frame export, and bounded contact-sheet generation, then render safely to MP4/H.264.

## Scope

### In scope

- `.magicclip` project schema v1 with future-schema read-only behavior.
- Up to 64 source files and 256 timeline segments.
- Trim source start/end.
- Cut a middle interval by splitting a segment into two bounded segments.
- Combine and reorder multiple clips.
- Duplicate/remove segments.
- Per-segment volume 0–200%; mute is volume zero.
- Per-segment normalized crop rectangle.
- Project output resize with even H.264 dimensions.
- Native MediaComposition preview.
- MP4/H.264 render through a same-directory `.partial.mp4` and atomic promote after success.
- Capture one frame as PNG.
- Contact sheet as PNG with bounded cell count and pixel budget.
- Dedicated Clip Editor window; recorder 4.3 remains unchanged.

### Explicitly out of scope for 4.4

- Playback speed/retiming (#89): MediaClip does not provide a render-time speed primitive.
- Audio extraction (#92).
- Title cards/text/shape overlays (#94/#95).
- Post-recording zoom/blur/tracking (#96–#98).
- Cross-format video conversion (#99).
- FFmpeg or cloud dependencies.

## Architecture

### Core: deterministic edit model

`Magic.Capture.Core.VideoEditing` owns immutable project/source/segment/crop models and policy helpers. It validates path IDs, bounded counts, time ranges, crop percentages, output size, volume, cut operations, timeline duration, contact-sheet plans, and project schema compatibility. Core never opens media files.

### App: MediaComposition bridge

`VideoEditCompositionService` resolves each segment source to `StorageFile`, creates a fresh `MediaClip`, applies trim/volume, attaches `VideoTransformEffectDefinition` for crop/output-size normalization, and appends clips in project order. A MediaClip instance is never reused in more than one composition slot.

Rendering creates a hidden same-directory partial file, uses `RenderToFileAsync(..., MediaTrimmingPreference.Precise, profile)`, checks the transcode result and non-zero size, then promotes the partial only after successful completion.

### Preview and image exports

Preview uses `GeneratePreviewMediaStreamSource`. Frame export and contact sheets use `GetThumbnailAsync/GetThumbnailsAsync`, decode to BGRA8, then encode PNG locally. Contact-sheet planning has hard bounds before allocating the canvas.

### Persistence

`VideoEditProjectStore` writes UTF-8 JSON transactionally via temp + replace. Schema v1 is current; future schema is returned as read-only and must never be overwritten by 4.4.

### UI

A new `VideoEditorWindow` owns transient UI state. It shows a timeline list, preview, clip controls, output dimensions, project open/save, MP4 export, frame export, and contact-sheet export. Editing changes only the model until export.

## Failure and safety rules

- Missing source files fail before render.
- Segment ranges outside the probed source duration fail before composition assembly.
- No partial render is promoted after cancellation/failure.
- Existing destination is replaced only after successful render promotion.
- Contact sheets reject excessive frame counts/pixel budgets before allocation.
- Future `.magicclip` schema is read-only.
- No recorder journal/schema changes in 4.4.

## Verification

- New Core tests cover trim/cut/count/output/crop/volume/contact-sheet/schema behavior.
- Repository verifier requires Core policy, project store, MediaComposition render/preview, crop transform, thumbnail/contact sheet path, UI handlers, and source-truth audit entries.
- Structural and lexical gates stay clean.
- Windows release checklist adds MediaComposition preview/render, mixed source dimensions, H.264 output, project recovery, cancellation, and long contact-sheet tests.
- .NET/WinUI runtime build remains an external Windows gate when unavailable in this environment.
