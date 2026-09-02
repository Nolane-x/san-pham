# Magic Capture Desktop — Comprehensive Upgrade Roadmap

**Baseline:** 2.1.0 source foundation wave  
**Primary product constraints:** desktop-first, local-first, low idle cost, fast interaction, stable resident lifetime, excellent keyboard/mouse UX, no mandatory account/cloud service.

## Architecture rule

New capabilities must be built on small deterministic primitives in `Magic.Capture.Core` and thin Windows adapters in `Magic.Capture.App`. Heavy optional runtimes such as recording codecs, local speech models, segmentation models or external tools must remain lazy/optional and must not increase idle CPU or materially increase startup memory for screenshot-only users.

## P0 — correctness, recovery and performance — implemented in 2.1 foundation

- authoritative image dimensions after workflow transforms;
- invalid Magic Recipe editor call removed;
- runtime settings normalization at load/save/resident-assignment boundaries;
- bounded content-aware ScreenGraph cache without cancellation poisoning;
- lightweight Capture Watch difference detector;
- LockBits blur/pixelate hot path;
- pairwise stitch matching and stitched-output memory safety limits;
- atomic JSON backup/recovery and corrupt History index rebuild;
- History path containment;
- bounded current-user-only resident IPC;
- project import limits for malformed/unbounded annotation data;
- cancellation preservation in persistence/cache paths.

## P1 — capture primitives — foundation implemented, expansion planned

Implemented/foundation:

- saved capture profiles;
- exact and recent regions;
- automatic vertical scrolling capture foundation;
- profile workflow/save-format propagation.

Next:

- post-drag resize handles + loupe/HUD;
- freehand/ellipse/polygon/multi-region capture;
- UI Automation element snapping;
- saved presets and per-profile hotkeys;
- sticky header/footer masking;
- dynamic-content detection and alignment retry;
- horizontal and 2D scrolling;
- Windows Graphics Capture GPU path with Desktop Duplication/GDI fallback;
- HDR/tone mapping and mixed-DPI torture coverage.

## P2 — editor, projects, privacy, History and workflows

Implemented/foundation:

- editable annotation object identity, lock, visibility, z-order, duplicate, nudge and rotation;
- `.magiccapture` package and validation;
- local sensitive-data detector + Smart Redact plan;
- rich History metadata/search foundation;
- workflow condition/retry/timeout primitives.

Next:

- resize handles, multi-select, group/ungroup, align/distribute, editable styling;
- Step/callout/speech bubble/magnify/spotlight tools;
- effect pipeline and presets;
- redact-before-copy/save/pin/workflow policy;
- FTS/local semantic History search;
- visual workflow builder, branching/fallback/subworkflow/trace/dry-run;
- document/step recorder project model and export.

## P3 — ScreenGraph, UI Automation and Compare

Implemented/foundation:

- UI Automation-ready ScreenGraph schema;
- stable UIA evidence node merge contract;
- MSE/PSNR/SSIM-style comparison metrics.

Next:

- native Windows UI Automation tree acquisition;
- OCR↔UIA evidence fusion;
- element snapping and object-aware Smart Move;
- text/table/layout diff;
- translation/scale/crop auto-alignment;
- heatmap/mask/blink/ignore-region compare modes;
- historical-version compare reports.

## P4 — optional recording/video subsystem

Planned as an optional/lazy subsystem:

- region/window/monitor/desktop recording;
- system + microphone audio;
- cursor/click visualization and webcam PiP;
- hardware encoding through Windows Media Foundation first;
- crash-recoverable recording journal;
- small local video editor: trim/cut/crop/resize/speed/mute/frame export;
- optional local subtitle generation via user-owned local runtime/model.

Recording must not add an always-on encoder, FFmpeg process or model to screenshot-only startup.

## P5 — long-tail desktop utilities and formats

Planned only after P0–P4 quality gates:

- WebP/PDF/AVIF/HEIF where platform/runtime support is reliable;
- target-size image optimization;
- safe allowlisted local external-action profiles;
- Explorer verbs/file associations/CLI expansion;
- color picker/ruler/magnifier/window/UIA inspector;
- background removal and local segmentation as optional model packs;
- scanner/WIA and niche utilities only when they do not compromise simplicity.

## Non-goals

Magic Capture Desktop will not require a Magic Capture account, hosted screenshot storage, subscription, developer-hosted AI credits, mandatory analytics, cloud sync, social feed or team server. Cloud AI remains user-configured BYOK; local AI remains BYOM/local-runtime based.

## Release gates

A source-level verifier is not a Windows build. Every release still requires:

1. `python scripts/verify-repo.py` clean;
2. .NET/xUnit tests on Windows;
3. Release x64 and ARM64 builds;
4. XAML compilation;
5. real mixed-DPI/multi-monitor capture smoke;
6. Store identity/licensing tests;
7. resident soak, sleep/resume and recovery tests;
8. long scrolling/huge-image/low-memory fixtures.
