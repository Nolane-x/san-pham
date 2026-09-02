# Magic Capture Desktop 1.1.1 — Release Candidate Hardening

Version 1.1.1 is a release-quality wave on top of the 1.1 commercial architecture. It deliberately avoids adding heavy background processing or AI.

## User-facing improvement

- History now has instant local search across OCR preview, barcode preview, source kind, dimensions, capture path/file name and capture date.
- Search is metadata-only and performs no background recognition or indexing.

## Release hardening

- Added centralized release metadata in `release/version.json`.
- Synchronized app assembly/file version and MSIX development manifest to `1.1.1` / `1.1.1.0`.
- Added `scripts/store-preflight.ps1`; Store packaging now refuses the development MSIX identity/publisher.
- `build.ps1`, `test.ps1` and `pack.ps1` fail fast on repository-verifier errors.
- Added deterministic cross-platform `scripts/source-release.py` with ZIP integrity test and SHA-256 output.
- Windows CI now uploads per-architecture build artifacts and runs a source-release dry run.
- Added `docs/WINDOWS_RELEASE_CHECKLIST.md` covering Free, Plus, Pro, startup, single-instance, DPI and Store-flight validation.

## Commercial model unchanged

- Free forever.
- Plus trial only for exactly 168 hours; not sold and never auto-renews.
- Pro Lifetime is the only paid tier.
- US Pro MSRP: $29.99.
- US launch offer: $19.99 for 90 consecutive days.
