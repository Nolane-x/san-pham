# Magic Capture Desktop 4.8 — Local Step Recorder & Documentation Builder Design

## Goal
Turn ordinary UI interactions into an editable local documentation project without cloud services, background polling, typed-text logging, or mandatory AI.

## Product scope
4.8 implements the `3.1-step-recorder-docs` feature cluster as one coherent local-first subsystem:

- explicit Start/Stop Step Recording;
- bounded low-level mouse click tracking and safe keyboard-action tracking;
- click-adjacent capture with UI Automation target detection;
- deterministic crop/description/title generation;
- automatic step numbering and click marker overlays;
- editable step order/add/remove/merge;
- reopenable `.magicdoc` project packages;
- long PNG, PDF, DOCX, HTML, Markdown + images, and self-contained offline HTML tutorial export.

The recorder is session-scoped. It is created only after the user explicitly presses Start and is disposed on Stop/window close. It never becomes a resident startup service.

## Architecture

### Core authority
`Magic.Capture.Core.Documentation` owns all deterministic behavior and safety limits:

- immutable documentation project/step models;
- stable project schema and validation;
- click crop planning and UIA target selection projection;
- deterministic title/description generation;
- step numbering and merge/reorder/edit operations;
- HTML/Markdown/DOCX package writers that consume already-encoded image payloads;
- archive-entry and export-size limits.

Core must not reference WinUI, Windows hooks, `System.Drawing`, Store APIs, HTTP, or AI providers.

### App adapters
`Magic.Capture.App.Documentation` owns Windows-specific integration:

- `StepRecorderInputTracker`: WH_MOUSE_LL plus a safe WH_KEYBOARD_LL subset. It records click coordinates/button and only named navigation/command key gestures produced by `RecordingSafeKeyFormatter`; it never stores printable typed text and ignores UIA password controls.
- `StepRecorderService`: resolves monitor/window/UIA evidence at click time, computes a bounded capture region, captures through the existing `ScreenCaptureService`, and creates a `DocumentationStep`.
- `DocumentationProjectStore`: atomic `.magicdoc` ZIP package with `manifest.json` and bounded `steps/*.png` entries.
- `DocumentationExportService`: bitmap composition for long PNG, existing `PdfImageDocumentWriter` for PDF, and Core writers for DOCX/HTML/Markdown/offline tutorial.

### UI
A dedicated `DocumentationWindow` keeps the already-large `MainWindow` from becoming the subsystem implementation surface. `MainWindow` gets only a small launcher card/button.

The window contains:

- Start / Stop recording;
- current status and safe-key privacy explanation;
- ordered step list with thumbnail, number, title and description;
- move up/down, drag reorder, add image, remove, duplicate, merge;
- title/section/step text editing;
- Save/Open `.magicdoc`;
- export menu for long PNG, PDF, DOCX, HTML, Markdown bundle and offline HTML.

## Data model

`DocumentationProject` schema v1:

- `ProjectId`, `CreatedUtc`, `ModifiedUtc`;
- `Title`, optional `Subtitle`;
- optional header/footer/logo metadata;
- `IReadOnlyList<DocumentationStep>`;
- template metadata only; no executable/script/provider fields.

`DocumentationStep`:

- stable `Id`;
- captured UTC timestamp;
- image key (`steps/<id>.png` in package);
- source image width/height;
- optional UIA control metadata: control type, name, automation id, process/window title;
- click position in image-local pixels and mouse button;
- optional safe key gesture label;
- editable `Title`, `Description`, `Section`;
- step ordinal is derived from list order, never persisted as an independent mutable counter.

## Capture policy

At a click at desktop point `P`:

1. Resolve the containing monitor.
2. Acquire a bounded UIA snapshot using the existing service/catalog and select the smallest/deepest target containing `P`.
3. Prefer a crop around the target bounds plus 48 px padding, clamped to the monitor.
4. If target bounds are unavailable/unsafe, use a 960×640 click-centered region clamped to the monitor.
5. Clamp final capture to at most 1920×1200 and at least 160×120 when monitor space permits.
6. Capture once through the existing backend router. No repeated polling.
7. Project the click into the captured image and add a non-destructive click-marker annotation at export/display time.

Click coalescing prevents double-click bursts from producing duplicate steps inside 180 ms / 8 px unless the mouse button changes.

## Description generation

No AI is required. Core produces concise deterministic text:

- named Button → `Click “Save”.`
- named CheckBox → `Toggle “Remember me”.`
- named Edit/ComboBox → `Select or edit “Format”.`
- unnamed control → `Click the <control type>.`
- fallback → `Click the highlighted area.`

The user can edit every generated title/description. Pro AI can remain a future optional refinement, but 4.8 does not call AI automatically.

## Keyboard privacy

The keyboard hook exists only while Step Recording is active and stores only a bounded allowlist of safe gestures such as Enter, Escape, Tab, arrows, function keys and modifier combinations that `RecordingSafeKeyFormatter` already exposes. Printable character keys without Ctrl/Alt/Win are discarded. No key sequence buffer exists. If the clicked UIA node reports `IsPassword=true`, both value and keyboard gesture association are omitted for that step.

## Project package

`.magicdoc` is a ZIP with fixed canonical names:

- `manifest.json`;
- `steps/<step-id>.png`;
- optional `logo.png`.

Limits:

- 512 steps;
- 32 MiB per image;
- 512 MiB total image payload;
- 4 MiB manifest;
- 128 KiB title/description aggregate per step;
- canonical entry names only; no `..`, absolute paths, backslashes, drive prefixes or duplicate entries;
- atomic temp-file promotion on save.

Unknown future schema versions are read-only/rejected for editing rather than silently downgraded.

## Export

- **Long PNG:** vertically stacks rendered step cards with bounded width and total decoded pixel budget. If the result would exceed 150 million pixels, export is rejected with a clear message rather than allocating an unsafe bitmap.
- **PDF:** each rendered step image becomes one page using the existing bounded PDF writer.
- **DOCX:** Core writes a minimal Office Open XML package with text plus PNG/JPEG media relationships; no Office installation required.
- **HTML:** folder export with `index.html` + `images/`.
- **Markdown:** `README.md` + `images/`.
- **Offline tutorial:** one self-contained HTML with base64 images and embedded CSS/JS limited to navigation/collapse; no remote assets, analytics or scripts.

All generated text is escaped for the destination format. DOCX XML uses entity escaping; HTML uses HTML escaping; Markdown escapes structural punctuation where needed.

## Entitlement

Step Recorder / Documentation Builder is an **AdvancedWorkflows (Plus / Pro)** capability. The 7-day Plus trial therefore exposes it, while Pro retains it permanently. No AI is required, so the feature remains useful after choosing local-only operation.

## Failure handling

- Hook callbacks always call `CallNextHookEx` and swallow only their own non-fatal processing failures.
- Capture failures produce a visible session error and do not stop the hook unless repeated fatal setup fails.
- UIA timeout/truncation degrades to click-centered capture.
- Save/export uses temp files/directories and only promotes completed outputs.
- Window close always disposes hooks and cancels in-flight captures.

## Verification

Source/static gates in this environment must prove:

- deterministic Core models/limits/operations and generated descriptions;
- archive path validation and future-schema handling;
- safe keyboard filtering contract;
- app hook lifecycle and `CallNextHookEx` contract;
- no resident startup wiring for Step Recorder;
- XAML handlers exist;
- `.magicdoc` package and export services are wired;
- feature-audit promotions match only implemented 4.8 capabilities;
- version/source/package metadata is synchronized.

Windows release gates still require real xUnit execution, WinUI/XAML compilation, hook/UIA behavior, multi-monitor mixed-DPI capture, and export opening in Word/browser/PDF readers.
