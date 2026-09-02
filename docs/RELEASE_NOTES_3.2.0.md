# Magic Capture Desktop 3.2.0 — Image Effects 2.0 Source Release Notes

Magic Capture Desktop 3.2.0 expands the existing local image-effect pipeline and adds bounded canvas/compositing tools without adding startup work, background processing, cloud services, native codecs or new runtime dependencies.

## Pixel and neighborhood effects
- Hue
- Vibrance
- Per-channel RGB color balance
- Sharpen
- Noise reduction
- Edge detection
- Bounded block mosaic
- Existing brightness/contrast/gamma/exposure/saturation/grayscale/sepia/invert/posterize/threshold remain composable in the same 32-step pipeline.
- Neighborhood effects reuse a single scratch BGRA buffer across an invocation.

## Canvas/compositing operations
- Torn edges and fade edges
- Reflection
- Four border presets
- Text watermark and local image/logo watermark
- Date/time and capture-information stamps
- Plain-border auto-crop
- Expand canvas
- Transparent-color removal with tolerance
- Exact color-key removal
- Arbitrary-degree rotation with bounded output canvas

## Effect packs
- Data-only `.magiceffect` JSON format
- Schema version 1
- Maximum payload 64 KiB
- Maximum 32 normalized steps
- No executable paths, scripts or shell expansion
- Import preloads the actual effect-pipeline dialog; export persists the last applied/imported pipeline.

## UX/performance
- All features are on-demand from Utilities.
- Batch effect execution remains sequential over History items.
- Screenshot capture, tray idle and startup paths are unchanged.
- Pixel/canvas allocations remain guarded by `ImageWorkloadLimits`.

## Feature ledger
3.2.0 promotes 21 source features to `Done` and keeps `Mosaic styles` Partial because only one bounded block-mosaic style is implemented.

Source ledger after this wave: **243 / 660 Done**.
