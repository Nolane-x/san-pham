# Magic Capture Desktop 2.1.0 — Source Foundation Release Notes

2.1.0 is a stability-first source wave. It deliberately builds reusable desktop primitives before large optional subsystems such as recording.

## Capture and UX

- Saved Capture Profiles with source, exact region, cursor, delay, post-capture action, optional workflow and save format.
- Recent/exact region shortcuts in Control Center.
- Automatic vertical scrolling capture foundation using local input synthesis, settled frame capture, deterministic change detection and pairwise stitching.

## Editable editor/project foundation

- Stable editable annotation layer IDs with lock/visibility/z-order/duplicate/nudge/rotation operations.
- Layer sidebar in the editor.
- `.magiccapture` editable ZIP project format with versioned manifest and base image.
- Project validation now rejects oversized/malformed layer, point, numeric and metadata payloads before rendering.
- Smart Redact creates local editable redaction layers from deterministic sensitive-data detection.

## Workflow, History and Compare

- Workflow conditions, bounded retries and per-step timeouts.
- Current transformed image/dimensions propagate correctly through save/pin/editor/AI/destination steps.
- History metadata/search foundation for title, notes, tags, favorite and source fields.
- Corrupt History index recovery and safe path containment.
- Lightweight Capture Watch frame-difference path.
- MSE, PSNR and SSIM-style compare metrics.

## Resident reliability and performance

- Settings are normalized at persistence and resident-state boundaries.
- ScreenGraph cache is bounded/content-aware and cannot be poisoned by a cancelled build.
- AI result cache preserves cancellation and serializes clear/read/write operations.
- Blur/pixelate use locked pixel buffers instead of per-pixel GDI+ access.
- Scrolling stitch matching decodes source frames pairwise and enforces safe output limits.
- Single-instance command pipe is current-user-only and rejects oversized payloads.
- Capture Watch CTS resources are released on completion and shutdown.

## Verification status

`python scripts/verify-repo.py` is the source/static gate. Windows `.NET 10 + WinUI 3` compile, xUnit, XAML, x64/ARM64 package and real hardware tests remain mandatory before calling 2.1.0 a public Windows release.
