# Magic Capture Desktop 2.5.0 — Source Release Notes

Magic Capture Desktop 2.5.0 is an output, image-optimization and local-utility wave. The new capabilities are deliberately on-demand: they do not add a resident codec process, background file indexer, clipboard watcher, model runtime, or periodic worker while the app is idle.

## Output and clipboard

- Single-image PDF export through a small deterministic JPEG-backed PDF writer in Core.
- Multi-page PDF from selected History captures.
- PDF contact sheets.
- Capture Profile `PDF` save path.
- Copy PNG as Data URI or raw Base64 text.
- Copy the History image file itself, its full path, or its containing-folder path.

## Image optimization

- JPEG quality compressor.
- Target-file-size JPEG optimizer with bounded quality binary search and bounded resize fallback.
- PNG lossless re-encode that keeps the original bytes when re-encoding is not smaller.
- Opt-in lossy PNG channel-precision reduction.
- Exact-dimension resize.
- Batch JPEG target-size export with collision-safe filenames and before/after byte totals.

## Local generators and inspectors

- Local QR and Code 128 generators.
- SHA-256 file comparison.
- Bounded Markdown directory index generation with depth/entry limits and reparse-point avoidance.
- On-demand Clipboard format/text viewer.
- Window inspector exposing HWND, process, class, title and bounds.
- Full-screen monitor test window with solid colors, gradient, color bars and grid patterns.
- Image pixel/color statistics.
- One-shot external editor launcher using an explicit `.exe` picker and `ProcessStartInfo.ArgumentList`; no command shell interpolation or saved executable permission is introduced.

## Release engineering

- Added `scripts/verify-structure.py` to parse XML/XAML/MSBuild/JSON, validate paired XAML event handlers and independently verify the exact 660-feature ledger.
- `scripts/source-release.py` now requires both repository and structural verifiers before creating the deterministic source archive.
- Exact feature ledger advances to **151 / 660 Done**. `Foundation`, `Partial`, `Missing` and `ReleaseTest` remain explicitly separate.

## Verification boundary

The source-generation environment is Linux and does not include .NET 10, WinUI, the Windows SDK or MSIX build tooling. Repository/static/structural gates can be executed here; xUnit execution, C# compilation, XAML compilation, x64/ARM64 builds and real Windows runtime smoke tests remain mandatory release gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
