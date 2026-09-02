# Magic Capture Desktop 2.6.0 — Source Release Notes

Magic Capture Desktop 2.6.0 introduces a bounded, on-demand image-effect pipeline. An image is decoded once, effect steps operate sequentially on the same BGRA buffer, and the result is encoded once. Nothing runs while the editor/utilities are closed.

## Effect pipeline

- Brightness.
- Contrast.
- Gamma.
- Exposure.
- Saturation.
- Grayscale.
- Sepia.
- Invert.
- Posterize.
- Threshold.
- Up to 32 ordered steps per pipeline.
- Five built-in effect presets.
- Apply to one selected History capture or batch-apply the same pipeline to up to 500 selected captures.
- Batch export uses collision-safe PNG filenames and reports failures without aborting the whole batch.

## Deliberate scope boundary

Hue, vibrance, color balance, sharpen, denoise, edge detection and geometry/decorative effects are not marked complete merely because the pipeline exists. They remain separate ledger items until their algorithms and end-user controls are implemented.

## Feature ledger

The exact 660-feature audit advances to **164 / 660 Done**. This number counts only end-to-end source paths; Windows/hardware release-test items remain separate.

## Verification boundary

The generation environment still lacks .NET 10/WinUI/Windows SDK. Repository and structural verifiers pass here; real compilation, xUnit, XAML, x64/ARM64, image-fixture and MSIX tests remain required on Windows.
