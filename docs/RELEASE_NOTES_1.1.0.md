# Magic Capture Desktop 1.1.0 — source release notes

This source release restructures the project around the final tray-first product direction and commercial model.

## Product and lifecycle

- Official name is **Magic Capture Desktop**.
- `Win + Shift + X` is the primary freeze-capture entry point.
- Closing Control Center hides it to tray; tray Exit terminates the resident process.
- Packaged Start-with-Windows support launches resident/hidden.
- Single-instance guard prevents duplicate tray icons and hotkey owners.

## Free / Plus / Pro

- Free remains usable forever.
- First successful launch starts a 168-hour Plus trial.
- Plus is not sold, never auto-renews, and requires no payment method.
- Pro Lifetime is the only paid tier and uses a Microsoft Store Durable add-on.
- US launch strategy: $19.99 for 90 consecutive days, regular MSRP $29.99.
- UI reads localized current Store pricing instead of hard-coding US amounts.

## Capture UX

- Freeze overlay exposes Copy, Save, Pin, Text, Table, QR, Edit, Color and More.
- Recognition work is demand-driven rather than run automatically on every capture.
- Pro adds repeat-last-region and fixed-aspect selection presets.

## Workspaces and power tools

- Plus: table extraction/structured export, QR/barcode, vertical stitching, advanced editor tools, advanced image formats and unlimited trial pins.
- Pro: Compare Workspace (side-by-side, overlay slider, deterministic difference), click-through pins and unlimited-history options.

## Packaging

- MSIX packaged desktop application.
- x64 and ARM64 targets.
- `runFullTrust` for tray/global hotkey/Win32 capture integration.
- Per-Monitor-V2 DPI awareness.
- Modular Windows App SDK package references; no AI/ML/Search component package dependency by design.

## Verification boundary

Static repository verification passes in the Linux generation environment. Windows compilation, xUnit execution, WinUI XAML compilation, MSIX creation, Store purchase behavior and real Windows capture/OCR/tray smoke tests remain mandatory before public Store submission.
