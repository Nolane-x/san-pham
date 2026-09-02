# Magic Capture Desktop 3.2 Image Effects 2.0 Design

## Goal
Expand the existing local image-effect pipeline into a bounded, reusable image transformation subsystem without adding idle CPU/RAM, native codecs, cloud services, or startup work.

## Architecture
1. `ImageEffectPipeline` remains the ordered pixel-processing contract. Add hue, vibrance, RGB balance, sharpen, denoise, edge detect, and mosaic with normalized bounded parameters.
2. `ImageEffectPipelineService` decodes once, applies all simple pixel effects in one BGRA buffer, and uses bounded scratch buffers only for neighborhood effects.
3. `ImageCanvasOperationsService` owns geometry/compositing effects that do not fit a per-pixel step: borders, torn/fade edges, reflection, watermarks/stamps, auto-crop, expand canvas, transparency/color-key removal, arbitrary rotation.
4. `ImageEffectPackSerializer` exports/imports only the portable ordered pipeline steps. Packs are bounded JSON, schema-versioned, duplicate/unknown-safe, and never contain executable paths.
5. Utilities expose these operations through compact dialogs. Batch remains sequential; no batch loads all images into memory.

## Reliability constraints
- Pixel-processing inputs must pass `ImageWorkloadLimits` before allocation.
- Pipeline max 32 steps; effect pack max 64 KiB; text fields max 512 chars.
- Neighborhood effects reuse at most one scratch BGRA buffer plus the working buffer.
- Canvas expansions/rotation validate resulting pixel count before allocating.
- Watermark image is local file input only, bounded before read/decode.
- Transparency operations preserve alpha; no hidden flattening.
- Default screenshot path and editor startup path are unchanged.

## Features targeted
#176 Hue, #178 Vibrance, #179 Color balance, #183 Sharpen, #184 Noise reduction, #185 Edge detection, #188 Mosaic styles, #191 Torn edges, #192 Fade edges, #193 Reflection, #194 Border presets, #195 Watermark text, #196 Watermark image/logo, #197 Date/time stamp, #198 Capture information stamp, #199 Auto-crop plain borders, #200 Expand canvas, #201 Make background transparent, #202 Tolerance-based transparent-color removal, #203 Rotate arbitrary degrees, #204 Color-key removal, #207 Import/export effect packs.
