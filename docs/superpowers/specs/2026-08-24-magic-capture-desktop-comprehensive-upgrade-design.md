# Magic Capture Desktop Comprehensive Upgrade Design

**Date:** 2026-08-24  
**Product identity:** Magic Capture Desktop  
**Design status:** Pre-approved by the user in the request (“tôi duyệt hết”).

## 1. Goal

Evolve Magic Capture Desktop from a strong screenshot/OCR/AI utility into a mature local-first desktop capture platform without sacrificing its defining constraints: low idle cost, fast capture latency, deterministic/local processing by default, stable Windows behavior, excellent UX, and user-owned data.

The 660-item capability survey is treated as a backlog of capabilities, not 660 independent UI controls. Features are merged into reusable subsystems so one primitive unlocks many user-facing capabilities.

## 2. Non-negotiable product constraints

1. The user-facing product name is always **Magic Capture Desktop**.
2. Idle work must remain minimal. No new polling loop is allowed unless the user explicitly enables the owning feature.
3. Optional heavy runtimes (FFmpeg, local ML models, semantic embeddings) are loaded on demand and must not be required for screenshot-only use.
4. Deterministic/local processing remains the default. Cloud AI remains BYOK and direct-to-provider.
5. Core logic stays platform-neutral and unit-testable in `Magic.Capture.Core` whenever practical.
6. Win32/WinUI/platform code stays in `Magic.Capture.App` behind focused services.
7. Existing Free / Plus Trial / Pro Lifetime commercial semantics are preserved unless a later product decision explicitly changes them.
8. Data written to disk must use atomic or recoverable formats where practical.
9. Long-running operations accept cancellation and expose progress when they become user-visible.
10. New capabilities must degrade cleanly when Windows APIs, OCR language packs, protected windows, GPU paths, or optional local tools are unavailable.

## 3. Architecture strategy

### 3.1 Foundation-first instead of feature-max

The implementation is organized around reusable engines:

- **Capture Pipeline** — capture source, region/profile, engine selection, post-capture routing.
- **Image Pipeline** — immutable image payload + authoritative dimensions, transforms, effects, optimization.
- **Editable Project** — base image + annotation objects + analysis metadata + versioned manifest.
- **Recognition Graph** — OCR/table/barcode/signals + UI Automation nodes merged into ScreenGraph.
- **Privacy Pipeline** — deterministic sensitive-data detection and redaction policies before copy/save/pin/workflow/AI.
- **Workflow Runtime** — conditions, retry/timeout, typed values, trace, optional external actions.
- **Library Index** — rich metadata, tags/favorites/sessions and local search primitives.
- **Compare Engine** — pixel, structural and graph-aware comparison.
- **Long Capture Engine** — adaptive scrolling, overlap confidence, sticky-region suppression, recovery.
- **Recording Engine** — optional Windows Graphics Capture + Media Foundation/FFmpeg subsystem loaded only when used.

### 3.2 Performance model

Startup constructs only lightweight services. Expensive state is lazy. Image operations avoid `GetPixel`/`SetPixel` inner loops and use contiguous buffers/LockBits. History and project metadata remain bounded and atomically written. Background work is cancellation-aware and throttled.

## 4. Priority decomposition

### P0 — correctness, responsiveness, release confidence

- Fix unresolved Magic Recipe editor call.
- Keep `CaptureAsset` pixel metadata synchronized whenever workflows replace image bytes.
- Replace pathological annotation blur/pixelation pixel loops with buffer-based processing.
- Add static verifier checks for known cross-file contracts and project naming.
- Add regression tests for all new core primitives.

### P1 — capture/project foundations

- Versioned capture profiles with exact regions and future hotkey/workflow/output fields.
- Saved/recent region model with normalization/clamping.
- Versioned `.magiccapture` project manifest preserving base image, annotation objects and optional analysis artifacts.
- Annotation object identity, visibility, lock, opacity, rotation, fill/stroke/text style metadata without forcing immediate renderer support for every future style.
- Project autosave/recovery hooks remain lazy and editor-scoped.

### P2 — privacy, workflow and library depth

- Deterministic sensitive-data classifier for e-mail, phone, IP, card/Luhn, JWT, private-key markers and user patterns.
- Redaction plan made of editable annotation layers.
- Workflow v2 primitives: conditions, retry count/delay, timeout and explicit skip semantics.
- Rich history metadata: title, notes, tags, favorite, session/source metadata while keeping old index JSON deserializable.
- Search expands across new metadata without introducing a database dependency yet.

### P3 — ScreenGraph and compare moat

- UI Automation node schema and deterministic merge into ScreenGraph with stable IDs and OCR association hooks.
- Compare metrics core for MSE/PSNR/SSIM-like structural score and perceptual fingerprints; app renderer can layer heatmaps later.
- Evidence IDs remain stable and only real graph nodes can be cited by AI.

### P4 — capture breadth and recording

- Automatic vertical/horizontal/2D scrolling with adaptive overlap, sticky-region handling, dynamic-content detection and retry.
- UIA element snapping and object capture.
- GPU capture path with Desktop Duplication/GDI fallbacks and HDR/tone-map strategy.
- Optional recording service for region/window/monitor, audio, webcam, hardware encoding and crash recovery.

### P5 — long-tail maturity

- Local video editor, subtitles, document generator, explorer integration, design utilities, more formats, optimization, scanner/device capture, localization/accessibility, and torture-test matrix.

## 5. Wave implemented by this plan

This source wave implements the high-leverage foundation subset that can be safely integrated without requiring Windows-only runtime execution in the current Linux build environment:

1. P0 bug fixes and image metadata correctness.
2. Annotation model v2 primitives and mutation helpers.
3. `.magiccapture` project manifest/package service.
4. Capture profile + saved/recent region core model.
5. Sensitive data detector and redaction planning primitives.
6. Workflow v2 condition/retry/timeout core contracts, with executor integration where low-risk.
7. History metadata v2 and expanded search.
8. UIA-ready ScreenGraph node model/merge input.
9. Core compare metric primitives.
10. Performance rewrite for blur/pixelate.
11. Expanded repository verifier and documentation.

## 6. Data compatibility

All record extensions use optional/defaulted fields so existing JSON can deserialize. Workflow schema version 1 remains valid; v2 adds optional execution policy fields and validator support. `.magiccapture` starts at schema 1 and refuses unsupported future major schemas rather than guessing.

## 7. Error handling

- Package reads validate manifest schema, image entry existence and path traversal.
- Capture regions normalize and clamp before use.
- Sensitive data detection never mutates an image; it emits a plan.
- Workflow timeouts use linked cancellation tokens; optional steps record failure/skip without crashing the host.
- History read corruption remains recoverable; future repair tooling can rebuild thumbnails/index.

## 8. Testing and verification

Core behavior is developed test-first. Because this environment has no preinstalled .NET SDK and cannot execute a WinUI Windows build natively, verification has three tiers:

1. Run `dotnet test` for `Magic.Capture.Core.Tests` if a local .NET 10 SDK can be provisioned.
2. Run `python scripts/verify-repo.py` for repository contracts/XML/static invariants.
3. Run targeted static scans for unresolved application method references and malformed project/package entries.

A real Windows x64/ARM64 CI build remains the release gate for WinUI/native behavior.

## 9. UX principles

- Progressive disclosure: common capture actions stay one-click; advanced controls live in profiles/editor/workflows.
- Preserve muscle memory and existing hotkeys.
- No modal prompt on routine local actions.
- Privacy actions are previewable and undoable.
- Long operations show progress/cancel only when needed.
- Heavy optional features are discoverable but do not impose runtime cost until opened.

## 10. Explicitly excluded product directions

No Magic Capture cloud account, hosted screenshot storage, mandatory sync, developer-hosted AI, AI credits, subscription, or cloud-dependent analytics. User-owned files + local storage + BYOK/BYOM remain the operating model.
