# Magic Capture Desktop 3.3.0

## Compare 3.0

This release deepens the local comparison workspace without adding background work or cloud dependencies.

- 64-bit dHash perceptual distance with bounded sampling.
- On-demand content registration: detect meaningful content bounds, crop, and scale image B into image A's content rectangle before optional translation alignment.
- Local OCR semantic diff with bounded LCS word comparison.
- Changed-word highlighting on image B, capped at 256 overlays.
- OCR layout diff using bounded line matching instead of index-only comparison.
- Table-cell diff using the existing deterministic table reconstruction/document engine.
- HTML compare report export with pixel/perceptual/semantic metrics.
- Batch compare 2–32 images sequentially against one baseline; no batch image retention in memory.
- Latest History pair loader that prefers a previous capture with the same window/source identity.

`Select regions to ignore` remains incomplete and is deliberately not counted as Done.

## Quality constraints

All Compare 3.0 semantic paths are lazy/on-demand. Default pixel comparison performs no OCR. Content registration and batch work remain bounded and cancellable where applicable.
