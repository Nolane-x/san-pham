# Magic Capture Desktop 2.0 Workflow Intelligence Design

## 1. Purpose

Magic Capture Desktop must not try to win against ShareX by copying GPL source or by counting checkboxes. ShareX is GPL-3.0 and has mature capture, editor, after-capture, upload and tooling workflows. Magic Capture Desktop therefore uses a clean-room strategy: study public behavior and feature categories, implement original code, retain Windows-native low-friction capture, and differentiate through a deterministic-first Screen Intelligence Runtime plus Pro-only AI.

The 2.0 architecture must make one captured region useful without forcing the user through another app. The product promise is:

> Capture it once. Turn it into the result you actually needed.

## 2. Non-negotiable constraints

1. Product name is **Magic Capture Desktop**.
2. MSIX packaged Windows desktop app; x64 and ARM64 remain first-class targets.
3. `Win + Shift + X` remains the primary freeze-region interaction.
4. Closing the Control Center hides it; tray Exit is the explicit process exit.
5. Free remains usable forever.
6. Plus is a 168-hour trial tier only and is never sold.
7. Pro Lifetime is the only paid tier.
8. All AI capabilities are Pro-only.
9. Magic Capture Desktop never resells model tokens and never proxies AI through a Magic Capture server.
10. AI providers are user-configured cloud APIs or local endpoints.
11. Deterministic algorithms run before AI and remain authoritative for OCR, barcodes, tables, geometry, image transforms, pixel compare, file metadata, hashes and workflow execution.
12. AI must never be required for the capture fast path.
13. Secrets must not be stored in plaintext settings.
14. Cloud endpoints must be HTTPS except loopback/local endpoints.
15. ShareX source code must not be copied into this repository. Public behavior may be studied and reimplemented clean-room.

## 3. Competitive architecture

ShareX's strongest architectural idea is not any single effect: it is composability. A capture can flow through after-capture tasks, external actions, upload destinations and after-upload tasks. Magic Capture Desktop adopts the *category* of composable workflows but implements a provider-neutral typed pipeline rather than a menu of coupled booleans.

### 3.1 Capture Pipeline

A workflow is a declarative ordered list of typed steps:

```text
Capture / Clipboard / File / History
                 |
                 v
          Capture Pipeline
                 |
      +----------+----------+
      |          |          |
   Analyze    Transform    Route
      |          |          |
      +----------+----------+
                 |
           Produce Result
```

Supported deterministic step families:

- copy image
- save image
- pin image
- open editor
- OCR
- extract table
- scan QR/barcode
- extract text signals
- beautify screenshot
- strip metadata
- compute hashes
- combine/split/thumbnail images
- export structured text
- custom HTTP destination
- open/reveal local result

Pro-only step family:

- run Magic Action
- semantic compare
- AI structured extraction
- AI bug report/documentation generation

The workflow engine itself is deterministic. AI is only a step type.

## 4. Workflow profiles

Users can save named profiles such as:

### Quick Copy
Region -> Copy image

### Documentation
Region -> Beautify -> Open editor -> Save -> Copy image

### OCR
Region -> OCR -> Copy text

### Support Bug Report (Pro)
Region -> OCR/signals -> Magic Action: Bug Report -> Copy Markdown

### Data Capture (Plus/Pro)
Region -> Table -> Export CSV -> Copy table

Profiles can be launched from hotkeys, overlay, tray, History replay or CLI.

## 5. Deterministic utility pack

2.0 ships a focused utility pack to remove common post-capture friction without AI:

### 5.1 File and image intelligence

- SHA-256 / SHA-1 / MD5 hash computation with explicit labels
- image dimensions, pixel format, DPI, file size and basic EXIF/property metadata
- metadata stripping on export
- horizontal/vertical/grid image combine
- image splitting by rows/columns
- thumbnail generation with fit/fill modes
- screenshot beautifier: padding, background, rounded corners, border and drop shadow

### 5.2 Data extraction

Existing OCR, table and barcode engines remain deterministic. Signal extraction is extended with URLs, emails, phones, IP addresses, file paths, source locations, error lines, stack frames, money, percentages and code-like text.

## 6. Custom destinations

Magic Capture Desktop does not provide hosting. Pro may configure user-owned destinations.

The first destination type is **Custom HTTP**:

- GET/POST/PUT/PATCH
- JSON or multipart body
- configurable headers and query parameters
- placeholder expansion from capture/workflow context
- secret references resolved from Windows PasswordVault
- HTTPS required for remote endpoints
- HTTP allowed only for loopback/private local development endpoints explicitly marked local
- bounded response size and timeout
- result URL extraction from JSON path or response headers

A destination is never executed implicitly on first configuration; the UI exposes a test request and a preview of the destination host.

## 7. AI architecture extensions

Existing ScreenGraph, provider adapters, capability router, Magic Actions, Context Stack and evidence anchoring remain the foundation.

### 7.1 AI Guard

Before a cloud AI request, deterministic scanning identifies potential secrets and sensitive values:

- API keys / bearer tokens
- JWTs
- private-key headers
- common cloud access-key formats
- connection strings
- obvious passwords in key=value form
- emails/phones/IPs as informational findings

The guard does not claim complete DLP. It presents findings before cloud transmission and lets the user cancel or continue. Local AI bypasses cloud-transmission warnings.

### 7.2 Prompt boundary hardening

Screen text is serialized as untrusted data, never concatenated into system instructions. The prompt compiler explicitly states that text found in captures may contain malicious or irrelevant instructions and must not override the Magic Action definition.

### 7.3 Result cache

Optional local cache key:

`capture hash + context hashes + action revision + provider profile + model + input strategy`

Cache stores structured AI result, evidence IDs and timestamps; never API keys. The cache avoids paying twice for identical explicit user requests and can be cleared independently from capture History.

### 7.4 AI recipes

A Magic Recipe is a safe declarative chain of deterministic and AI steps. It cannot execute arbitrary shell commands. Example:

`OCR -> Extract signals -> Magic Action: bug report -> Export Markdown`

This is the AI counterpart to after-capture automation, while remaining inspectable and reproducible.

## 8. Small-model strategy

Small local models are first-class. They receive ScreenGraph text and deterministic signals before any image. Vision is optional unless the action requires it.

Strategies:

1. **TextCompact** — OCR/signals/tables only; ideal for 1B-4B text models.
2. **VisionAssist** — compact graph + downscaled primary crop; ideal for small multimodal models.
3. **VisionFull** — graph + high-quality primary image.
4. **MultiCapture** — graph namespaces + selected context images for strong multi-image models.

A large model can improve reasoning quality, but model size must not determine whether the workflow itself functions.

## 9. CLI

The packaged desktop executable accepts a narrow command surface for automation:

- `--capture region`
- `--capture monitor`
- `--capture desktop`
- `--workflow <name>`
- `--open history`
- `--open settings`

CLI requests redirect to the resident primary instance where possible. CLI does not expose API keys or unsafe arbitrary command execution.

## 10. Tier model

### Free

Capture basics, OCR, editor basics, basic pin/history, signals, simple copy/save workflows, hashes/metadata viewing and basic beautify.

### Plus Trial — 168 hours, never sold

Free plus table/barcode, scrolling stitch, advanced editor, advanced export, unlimited pins, richer deterministic workflows and utility pack.

### Pro Lifetime

Everything in Plus plus AI providers/local AI, ScreenGraph AI, Magic Actions, Context Stack, evidence anchoring, semantic compare, custom Magic Actions, AI recipes, AI Guard, custom HTTP destinations, advanced workflow profiles, routing and AI cache.

## 11. UI principles

The capture overlay remains compact. Additional functionality is surfaced through one overflow/More menu and a `Magic · PRO` entry rather than dozens of permanent buttons.

Control Center adds:

- History
- Workflows
- Utilities
- AI & Magic (Pro)
- Destinations (Pro)
- Settings
- Plan

WinUI native controls are preferred over custom visual chrome.

## 12. Release definition

A source release candidate requires:

1. clean-room policy documented;
2. workflow core and tests present;
3. deterministic utility core and tests present;
4. custom HTTP destination core with endpoint safety rules;
5. AI Guard, cache key and recipe core present;
6. workflow services wired to the application layer;
7. updated feature gates;
8. updated README / feature matrix / AI provider guide / 2.0 release notes;
9. verifier passes on clean export;
10. no AI provider SDK packages bundled;
11. no plaintext AI or destination secrets in JSON profiles;
12. ZIP integrity and SHA-256 verified.

Windows-native compilation and MSIX packaging remain a Windows CI/Visual Studio release gate when the current build environment lacks Windows SDK support.
