# Magic Capture Desktop 2.3.0 — Source Release Notes

Magic Capture Desktop 2.3.0 expands the local Library/History and Pin workflows without adding a resident database, cloud dependency, polling worker, or screenshot-only startup cost.

## 660-feature ledger

This source snapshot reports **112 / 660 Done**. `Partial`, `Foundation`, `Missing`, and `ReleaseTest` do not count as complete. The exact source-truth ledger is `docs/FEATURE_AUDIT_660.md` and `release/feature-audit-660.json`.

## History / Library 2.0

- pure Core History query engine;
- filters for date, capture type, dimensions, OCR/QR presence, favorite, session, source/app text and window text when metadata exists;
- newest/oldest/file-size sorting;
- per-process capture session IDs surfaced in the History list;
- batch delete with index/file consistency protection;
- batch tags with bounded normalized metadata;
- collision-safe batch export;
- bounded multi-image import normalized to local PNG History entries;
- corrupt duplicate IDs are deduplicated on load;
- clear/retention/delete keep records when the primary image is locked and cannot be removed.

## Pin 2.0

- zoom in/out with bounded layout scale;
- fit, actual-size, and reset commands;
- Copy, Save, and Edit commands using existing local services;
- reversible click-through state, with tray recovery while mouse input is ignored;
- opacity is persisted to normalized application settings.

## Exact-region presets

Built-in on-demand presets include 720p, 1080p, 1440p, 4K UHD, 1080-square, 1080x1350 social portrait, and 1080x1920 vertical/story while preserving exact X/Y/W/H entry and virtual-desktop clipping.

## Verification boundary

Repository/static verification can run in this environment. A real Windows machine with .NET 10, Windows App SDK/WinUI, x64/ARM64 packaging, DPI, picker, clipboard, and native-window validation remains mandatory before a Store release.
