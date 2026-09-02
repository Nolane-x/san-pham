# Magic Capture Desktop 2.2.0 — Source Release Notes

## Release truth

2.2.0 is a **source candidate**, not a claim that all 660 requested capabilities are complete. The exact ledger in `docs/FEATURE_AUDIT_660.md` reports **91 / 660 Done** at this source snapshot; Partial, Foundation and ReleaseTest entries do not count as Done.

The source-generation environment does not contain the Windows .NET/WinUI toolchain. Repository/static verification is available here; xUnit, XAML compilation, x64/ARM64 builds, MSIX packaging and native runtime smoke remain mandatory Windows release gates.

## Editor Objects 2.0

- multi-select annotation layers;
- group / ungroup;
- align and distribute;
- equal width/height;
- internal copy/paste and duplicate-many with fresh IDs;
- editable layer bounds;
- editable opacity, line/fill/text style;
- freehand resize scales point geometry instead of only its bounding box.

## Compare 2.0

- configurable difference threshold;
- fully-transparent pixel ignore policy;
- per-channel B/G/R/A mean absolute difference;
- grayscale difference, binary mask and heatmap;
- blink and before/after/diff triptych modes;
- bounded translation auto-alignment;
- existing MSE/PSNR/SSIM metrics retained.

All Compare work remains scoped to an open Compare session; no new resident background service is introduced.

## Local privacy pipeline

- opt-in redact-before-Copy;
- opt-in redact-before-Save;
- opt-in redact-before-Pin;
- opt-in redact-before-Workflow;
- Pixelate or Blur rendering;
- user sensitive-word list;
- bounded custom regular expressions with timeout/length/count limits;
- IPv6 detection added to deterministic sensitive-data detection;
- enabled outbound policies fail closed rather than silently exporting original pixels after a redaction failure.

History intentionally retains the original local capture; redaction applies only to explicitly configured outbound paths.

## Capture precision UX

- physical desktop X/Y/W/H HUD;
- eight post-drag resize handles;
- fixed-aspect corner resize behavior;
- Reselect without closing/reopening the overlay;
- persisted Dark/Light capture-overlay preference;
- geometry math moved into deterministic Core code.

## 660-feature audit

The original numbered request is preserved as an exact 1..660 ledger:

- `docs/FEATURE_AUDIT_660.md` — human-readable matrix;
- `docs/feature-audit/feature-backlog-660.json` — preserved request IDs/names/source lines;
- `release/feature-audit-660.json` — machine-readable status/evidence ledger.

2.2.0 counts:

- Done: 91
- Partial: 134
- Foundation: 270
- Missing: 143
- ReleaseTest: 22
- Total: 660

## Performance / resident-cost rule

2.2 adds no recording codec, FFmpeg process, ML model, polling loop or always-on worker. Editor, Compare and redaction work start only when the corresponding user action requires them.

## Mandatory Windows release gate

Before public release, complete `docs/WINDOWS_RELEASE_CHECKLIST.md`, including xUnit, XAML compilation, Release x64/ARM64 builds, mixed-DPI input/render smoke, privacy outbound-path tests, Compare timer lifecycle and resident soak tests.
