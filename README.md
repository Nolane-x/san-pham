# Magic Capture Desktop 4.16.0

**A tray-first Windows capture and screen-intelligence utility: `Win + Shift + X`, freeze, select, act.**

Magic Capture Desktop 4.16.0 adds **Work Recovery** across every current durable project-backed editor. Annotation projects, Documentation Builder `.magicdoc` projects and Video Editor `.magicclip` projects now use bounded local crash recovery with immutable revision snapshots and an atomically promoted journal pointer. Recovery is lazy, has no resident polling worker, never stores the original full project path, and opens recovered work as a copy so ordinary project files are never silently overwritten.

The product principle is:

> **Deterministic first. AI only where reasoning adds value.**

OCR, tables, QR/barcodes, color tools, image transforms, stitching, pixel comparison, History search, metadata/hashes, recovery and most workflow steps remain local deterministic operations. AI does not run when the app starts, while it idles in the tray, or merely because the user pressed the capture hotkey.

## Current source release

```text
Product:       Magic Capture Desktop
Version:       4.16.0
MSIX version:  4.16.0.0
Primary hotkey Win + Shift + X
Architectures: x64 + ARM64
Packaging:     MSIX
```

The generation environment for this source bundle is Linux and does not include .NET/Visual Studio/Windows SDK. Static repository gates can run here, but real WinUI compilation, xUnit execution, MSIX packaging and runtime smoke tests remain mandatory Windows release gates.

## 4.16 Work Recovery

A shared Core `WorkspaceRecoveryPolicy` defines the bounded journal contract for Documentation and Video Edit recovery: schema 1, eight active sessions per kind, 64 KiB maximum journals, fourteen-day lifetime, five-minute future-clock tolerance and kind-scoped `session-revision` snapshot names. The journal contains only recovery metadata and a display name; it does not persist the user's original full project path.

`DocumentationRecoveryStore` and `VideoEditRecoveryStore` write through the existing authoritative project serializers. Each new revision is written as a complete immutable `.magicdoc` or `.magicclip` snapshot first; only then is the small JSON journal atomically replaced. The previous snapshot is deleted only after the new pointer is durable. Corrupt, expired, oversized, mismatched and orphaned recovery files are bounded and pruned without traversing outside their dedicated LocalAppData roots.

Documentation Builder and Video Editor now use one-shot 1.5-second `DispatcherQueueTimer` autosave, revision counters, generation tokens and per-window write gates. Explicit save, project open and normal window close cannot race with an older autosave and erase newer edits. Whole-app exit preserves the latest completed recovery revision. Home surfaces independent Recover/Discard cards; Recover opens an unsaved copy and does not overwrite the source project. Future-schema video projects stay read-only and never enter autosave writes.

The source-truth ledger is now **464 Done / 46 Partial / 92 Foundation / 36 Missing / 22 ReleaseTest = 660**. Features **#606 Autosave editor project**, **#607 Restore editor after crash** and **#608 Restore unfinished document** move to Done. **#609 Restore unfinished recording** deliberately remains Partial because detecting an interrupted recording session is not the same as reconstructing/finalizing a partial MP4.

## 4.15.1 Quality Hardening

Settings writes now flow through one serialized `App` mutation authority using the newest in-memory snapshot. Child windows cannot persist or replace `AppSettings` directly. Native Region, Repeat and personal hotkeys are applied as one transaction before persistence; persistence is the commit point, rollback failures are surfaced explicitly, and Repeat is never restored when entitlement no longer permits it. Post-commit theme, retention, trigger and UI effects are explicitly masked and best-effort so a committed save is not later reported as failed.

Configuration import uses a strict settings reload after the archive transaction, prunes stale workflow/Magic Action/profile references, falls back to the previously active hotkeys when imported gestures conflict, disables dangling workflow triggers without deleting them, and reports reconciliation warnings. Startup performs a bounded catalog-aware reference check before initial hotkey registration, writing settings only when stale references actually exist. Workflow, Capture Profile, Magic Action, Local Action and Custom Destination deletion is fail-safe when another workflow/recipe/trigger still depends on the target.

The feature ledger is deliberately unchanged at **461 Done / 46 Partial / 95 Foundation / 36 Missing / 22 ReleaseTest = 660**. Windows CI now restores the WinUI app for each matrix platform before `--no-restore` build, while this Linux source-generation environment still treats real xUnit/WinUI/MSIX execution as a Windows release gate.

## 4.15 Settings & Personalization Runtime

Personal hotkeys use the same Win32 `RegisterHotKey` path as the primary capture shortcut but are bounded to 48 normalized bindings. Capture targets support direct Region/Foreground Window/Active Monitor/Virtual Desktop/Repeat actions and `profile:<id>` targets, so saved fixed-region or Scrolling profiles can be launched globally. Workflow and Magic Action IDs are validated before save, and registration is applied before persistence with rollback if Windows rejects any shortcut.

Editor personalization is runtime behavior, not metadata only: the Annotation editor opens with the configured default or last-used tool, named styles can be saved/applied/deleted locally, and the editor toolbar plus capture-overlay action bar honor normalized order/visibility allowlists. Seven Settings sections have independent reset actions. Per-monitor cursor/post-capture overrides and per-app executable-name rules are resolved at capture time; app rules store only `foo.exe`-style names and reference existing capture profiles.

## 4.14 History Intelligence & Organization

History organization is local and deliberately separate from the authoritative capture index. **Workspaces** provide a top-level grouping, **Folders** provide one level below a workspace, and **Collections** allow a capture to belong to multiple named sets without moving or duplicating its PNG. Deleting an organizer never deletes a capture. Bounds are enforced at the model/store boundary: 32 workspaces, 128 folders total, 64 folders per workspace, 128 collections, 5,000 members per collection and 32 collection memberships per capture.

The History query pipeline can filter by workspace/folder/collection, workflow id or actually-attempted Magic Action id, and can sort by local **Most used** activity. Activity persistence stores only bounded identifiers, counts and timestamps. AI/OCR output, prompts, image bytes, HTTP bodies and Local Action stdout/stderr never enter `history-library.json`. History open/workflow activity is best-effort and cannot make the primary History operation fail; transient organizer I/O is not quarantined or overwritten by mutation paths.

The new **Timeline** view always renders captures in descending chronological order regardless of the List sort selection. History supports multi-select in either view and a Library manager for create/rename/delete/assignment operations. Dragging image files or a folder onto History imports supported top-level images only, deduplicates paths and stops at 500 candidates per drop.

Window capture now carries an optional bounded executable path through `CaptureAsset` into `HistoryItem`. Process icons are extracted best-effort only from local drive-rooted executable paths, cached by SHA-256-derived key, capped to 256 local PNGs and 2 MiB per encoded icon, and are never required for capture durability or History loading. Portable History import preserves executable metadata, while icon resolution rejects UNC/network paths.

## 4.13 Workflow Control Flow & Safe Resume

Workflow Studio now saves schema-v5 custom workflows. `ForEachImage` takes a child workflow id in `Argument` and iterates a host-supplied image set with a hard **32-image** cap. The History workflow page adds **Run once with selected History as image loop**; each child receives `loop.index`, `loop.number` and `loop.count`. Nested loops collapse to the current child image, existing workflow cycle/depth guards remain active, and loop steps are forbidden from retrying as a whole so already-processed images cannot be replayed accidentally. `continueOnError=true` may continue after both returned child failures and non-fatal child exceptions.

Workflow traces now add only three resume-oriented metadata families: source `AssetId`, a canonical SHA-256 workflow fingerprint, and resume ancestry/cumulative safe-side-effect step ids. Failed traces can be resumed only while the exact workflow execution contract still matches and the original History capture still exists. Resume replays deterministic/interactive state-building steps, may ask for inputs again, and suppresses only previously completed `CopyImage`, `CopyText`, `SaveImage`, `PinImage` and `OpenEditor` steps. A completed or failed `RunMagicAction`, custom HTTP destination, Local Action, subworkflow or image loop makes the trace non-resumable because replay could duplicate external effects or skipping could lose required output. Repeated resume preserves the cumulative safe-side-effect set so a second resume cannot re-run an effect already suppressed by the first.

The resume trace store remains payload-free: it never persists capture pixels, OCR/AI text, variables, prompt answers, HTTP bodies, clipboard payloads, stdout/stderr or Local Action output.

## 4.12 Automation Triggers

Workflow automation can now be driven locally by six bounded trigger kinds: **Schedule**, **File Change**, **Clipboard Change**, **Foreground Window**, **Process Start**, and **Hotkey**. Schedule triggers use Windows Task Scheduler for the current interactive user at LIMITED privilege and launch the packaged `magiccapture.exe --trigger <id>` alias without `cmd.exe` or PowerShell. The scheduled CLI path is headless and accepts only Schedule triggers, so a stale Windows task cannot execute a trigger whose id was later reused for another kind.

Resident triggers are intentionally lightweight. File changes use `FileSystemWatcher`; clipboard and foreground-window changes are Win32 notifications/hooks; hotkeys use `RegisterHotKey`; only Process Start uses a two-second poll, and that timer exists only while an enabled process trigger exists. At most **64 triggers** and **16 workflow hotkeys** may be configured. Each trigger has a 1–3,600 second cooldown, one pending resident event at a time, and a hard circuit breaker of 20 accepted attempts in five minutes followed by a ten-minute suspension. Cooldown begins after an attempt completes, which prevents long clipboard/file workflows from immediately chaining into their own events.

Every run resolves the current workflow, tier and capture profile again immediately before execution. Automation requires `AdvancedWorkflows`; exact Region, Foreground Window, Active Monitor and Virtual Desktop profiles are allowed, while interactive Region and Scrolling profiles fail closed. Entitlement downgrade reloads the resident engine and removes its watchers/hooks/hotkeys/process timer. Trigger history retains only the newest **200 metadata records** (trigger id/name/kind, status, reason code and timing); it never stores changed file names, clipboard contents, foreground titles, command lines, capture pixels, OCR/AI values or workflow payloads.

## 4.11 Workflow Runtime v4

Workflow Studio now saves schema-v4 custom workflows with up to 24 typed parameters (`Text`, `Choice`, `Boolean`). Runtime resolution follows supplied values → workflow variables → parameter defaults → explicit interactive input; non-interactive hosts fail closed instead of inventing missing values. New steps are `PromptText`, `PromptChoice`, `Confirm`, `Delay`, and `RunWorkflow`. Subworkflows reuse the current image/context, reject cycles, and are bounded to four workflow levels.

History multi-selection now runs through a dedicated sequential `WorkflowBatchRunner` with a hard 500-capture cap and lazy per-item image loading. Studio dry-run executes deterministic local analysis while suppressing clipboard, file, pin, window, HTTP, AI, Local Action, prompt and delay side effects. `WorkflowTraceStore` keeps only the newest 100 local traces and persists timing/status metadata only — never capture pixels, OCR/AI text, runtime variable values, HTTP bodies, clipboard payloads or Local Action stdout/stderr. Preflight failures also produce payload-free trace metadata. Redaction graph traversal follows the shallowest executable path so nested workflows cannot bypass Redact-before-Workflow/Copy/Save/Pin through an alternate graph path.

Loop-over-images (#424), resume/checkpoint semantics (#432), Windows Task Scheduler and background trigger automation (#438–#444) are deliberately **not** claimed by 4.11.

## 4.9 Documentation Publishing

The Plus/Pro documentation workspace now completes the publishing surface started in 4.8. Steps support native WinUI drag reorder while retaining Move Up/Down as an accessible deterministic fallback. Projects expose four stable page templates — **Clean**, **Compact**, **Presentation**, and **Print** — plus authored header/footer metadata and an optional embedded logo stored inside the bounded `.magicdoc` package.

All six local export paths share the same publishing metadata. HTML, Markdown and self-contained offline HTML emit a generated table of contents with stable step anchors; DOCX writes a contents section plus real Word header/footer parts; long PNG and PDF use a template-aware overview card and template-aware step cards. At the existing 512-step PDF safety ceiling, the PDF exporter omits the extra overview page rather than exceeding the hard page cap. No new network dependency or cloud service is introduced, and the `.magicdoc` schema remains version 1.

## Product model

Magic Capture Desktop has three runtime tiers but only one paid product.

| Tier | Availability | AI | Purpose |
|---|---|---:|---|
| **Free** | Forever | No | Complete everyday capture foundation |
| **Plus** | First 168 hours only | No | 7-day trial of advanced deterministic tools; never sold |
| **Pro Lifetime** | One-time Microsoft Store durable add-on | **Yes** | Permanent power features, workflows, destinations and AI/Magic runtime |

Plus never asks for a payment method, never auto-renews and never converts into a charge. When Plus expires, the app remains Free and existing captures/settings are retained.

### Commercial baseline

```text
App                          Free forever
Plus                         7-day trial only; not sold
Pro Lifetime MSRP (US)       $29.99
Launch price (US)            $19.99
Launch-price duration        90 consecutive days
Subscription                 None
Developer AI token service   None
```

The application does not hard-code market price strings. The Plan page reads Microsoft Store localized pricing.

## Primary interaction: Freeze Capture Hub

`Win + Shift + X` is the normal way to use the product.

```text
Win + Shift + X
      ↓
freeze active monitor
      ↓
select Rectangle · Ellipse · Polygon · Freehand · Multi-region
      ↓
Copy · Save · Pin · Text · Table · QR · Edit · Color · Magic · Workflows
```

The fast path does not automatically run OCR, barcode scanning, table reconstruction or AI. The requested engine starts only when the user chooses the corresponding action.

### Overlay actions

- **Copy** — copy selected pixels immediately.
- **Save** — save the selected image.
- **Pin** — keep the capture as an always-on-top reference.
- **Text** — local Windows OCR.
- **Table · PLUS** — reconstruct a table from OCR geometry.
- **QR · PLUS** — local QR/barcode scan.
- **Edit** — annotation/transform editor.
- **Color** — deterministic pixel color sampling.
- **Magic · PRO** — ScreenGraph + Magic Actions using the user's configured local/cloud model.
- **Workflow: Quick Copy** — Free.
- **Workflow: OCR → Copy** — Free.
- **Workflow: Documentation · PLUS** — deterministic beautify → editor.
- **Workflow: Data Capture · PLUS** — table extraction → structured copy.
- **Workflow: Bug Report · PRO** — OCR/signals → evidence-aware AI bug report → Markdown copy.

Pro also adds fixed-aspect selection and repeat-last-region tooling.

## Resident Windows lifecycle

Magic Capture Desktop behaves like a utility rather than a document application.

- Start-menu launch opens the Control Center.
- Windows startup activation creates the resident tray/hotkey host without opening the Control Center.
- Closing the Control Center hides it to the tray.
- `Win + Shift + X` remains active while the UI is hidden.
- Single-instance command forwarding prevents duplicate tray processes/hotkey owners.
- Only **Exit Magic Capture Desktop** in the tray terminates the resident process.

The package uses full-trust desktop integration and a Per-Monitor-V2 process manifest for physical-pixel capture geometry.

# Deterministic intelligence

Magic Capture Desktop 2.0 deliberately keeps a large non-AI core.

## Capture

- region / freeze capture;
- foreground window;
- active monitor;
- full virtual desktop;
- capture cursor option;
- delays;
- repeat last region (Pro);
- fixed 1:1 / 16:9 / 4:3 selection (Pro);
- Capture Watch / timed repeat;
- change-aware Capture Watch (Plus/Pro).

## Recognition

- local Windows OCR;
- OCR word/line geometry;
- interactive OCR word / line / block hit-testing and copy;
- bounded search + screenshot highlights;
- Plain / Layout / Code reconstruction;
- installed-language selection + explicit cancellable OCR rerun;
- deterministic table header/type/anomaly inference;
- bounded editable Table Workspace with row/column edits, manual merge/unmerge, selected-cell TSV copy, local XLSX export and deterministic table diff;
- CSV/TSV dialect, locale-aware numeric output and Excel-safe TSV;
- table reconstruction;
- CSV, TSV, Markdown, HTML and JSON table serialization;
- QR/barcode recognition;
- deterministic text signals: URLs, emails, paths, stack frames, errors, money/percent and similar patterns.

## Imaging / editor

- crop, resize, rotate and flip;
- rectangle, ellipse, line/arrow, pen, highlight and text annotations;
- blur and pixelate;
- editable `.magiccapture` projects with session-scoped, debounced LocalAppData autosave recovery; recovery never overwrites the user's original project file;
- pin windows and opacity;
- Pro click-through pins with tray recovery;
- vertical screenshot stitching;
- side-by-side / overlay / deterministic pixel difference comparison;
- color sampling;
- metadata inspection and stripping;
- hashes;
- beautification;
- thumbnails;
- image combine/split utilities.

## History

History is local and searchable using already-stored metadata/OCR/barcode previews. No semantic cloud index is required.

# ScreenGraph — the Pro AI foundation

The main 2.0 differentiator is that AI does not receive only an opaque screenshot.

Magic Capture Desktop compiles deterministic analysis into a `ScreenGraphDocument` containing useful nodes/evidence such as:

```text
Capture
├── OCR lines/words + source rectangles
├── table structure
├── barcodes
├── URLs / emails / paths / errors / stack frames
├── capture geometry
├── bounded Windows UI Automation controls + hierarchy/state
├── UIA ↔ OCR word evidence correlation
└── evidence IDs
```

This matters for both quality and cost:

- text-only/small local models can reason over compact ScreenGraph text without image input;
- basic vision models receive ScreenGraph + a downscaled relevant image;
- strong multimodal models may receive richer primary/context images;
- evidence returned by the model is resolved back to deterministic source pixels.

# Pro AI — BYOK / BYOM

Magic Capture Desktop does **not** sell AI usage and does not proxy prompts through a developer-operated inference service.

A Pro user supplies their own provider/model:

- OpenAI Responses;
- Anthropic Messages;
- Google Gemini;
- OpenRouter;
- OpenAI-compatible endpoints;
- Ollama;
- LM Studio.

The provider architecture is capability-based rather than model-name-based. Model discovery is available where the endpoint supports it.

### Provider secrets

Cloud API credentials are stored via Windows `PasswordVault`; JSON profiles contain a secret reference, not the plaintext credential.

### Endpoint policy

- remote endpoints: HTTPS required;
- localhost/loopback endpoints: HTTP permitted for local runtimes such as Ollama/LM Studio.

### Routing

- **Active profile only**;
- **Prefer compatible local model**;
- **Best compatible capability**.

Capabilities include text, vision, multiple images, structured JSON, JSON Schema, reasoning/tool-like capabilities and local endpoint status.

### Small-model path

Small/local models are first-class rather than a degraded afterthought. The planner prefers deterministic ScreenGraph context and avoids pixels when they are not needed.

### AI Guard / privacy

Before cloud AI, deterministic AI Guard can warn about likely private keys, bearer tokens, JWTs, API-key assignments, password-bearing connection strings, password assignments and common PII-like text.

Cloud payload confirmation identifies the **provider actually chosen by the router**. Workflows/recipes cannot bypass this path. Capture Watch must not silently transmit screen content to cloud AI.

Captured screen text is delimited as untrusted source data so visible prompt-injection text is not intentionally promoted to application instructions.

OpenAI Responses requests set `store=false` by default in the native adapter; provider-specific retention/data policies still belong to the user's chosen provider/account.

# Magic Actions

Magic Actions are context-aware Pro operations, not a generic chatbot button.

Built-in categories currently include:

### General

Explain, summarize, translate, extract key facts, clean notes, ask about capture.

### Developer

Explain error, create bug report, extract stack trace, likely causes, debugging checklist, explain code, find likely bug, generate test ideas.

### Data

Explain table, find anomalies, describe trends, extract records.

### UI

Describe UI, UX review, visible accessibility review, UI documentation, acceptance criteria.

### Document

Extract action items and entities.

### Compare

Semantic compare between primary/context captures.

`MagicActionRecommender` uses deterministic ScreenGraph signals to rank useful actions **without spending an AI call**.

# Evidence Anchoring

Magic Action output may reference ScreenGraph evidence IDs.

```text
p:w18   primary capture word/node
c1:w4   first Context Stack capture
c2:s2   second Context Stack capture signal
```

The app resolves these IDs back to original image geometry. Evidence belonging to a context capture is not incorrectly painted over the primary image.

# Context Stack

Pro can collect supporting captures and run one Magic Action against a primary capture plus bounded context.

The planner adapts to the model:

- text-only → ScreenGraphs only;
- basic vision → limited relevant image set;
- strong multi-image vision → richer image context where useful.

# Workflows and Magic Recipes

Magic Capture Desktop 2.0 adds a declarative capture pipeline.

Workflow steps currently include:

```text
CopyImage
CopyText
SaveImage
PinImage
OpenEditor
RunOcr
ExtractTable
ScanBarcode
ExtractSignals
BeautifyImage
StripMetadata
ComputeHashes
ExportText
CustomHttpDestination
RunMagicAction
RunLocalAction
```

### Workflow Studio · PLUS / PRO

Version 3.6 adds a visual local workflow editor in the Control Center:

- create, edit, duplicate and delete custom workflows;
- drag/drop or button-reorder steps;
- enable/disable individual steps without deleting them;
- edit required/optional behavior, argument, output key, condition, options, retry count/delay and timeout;
- define bounded default workflow variables;
- import/export validated `.magicworkflow` files;
- pass runtime overrides from CLI with repeated `--var name=value`.

### Local Actions · PLUS / PRO

Local Actions let a workflow invoke a user-chosen local executable without turning Magic Capture into a shell runner. Each profile has bounded arguments, timeout, stdout/stderr limits and output-file size limits. The app launches `.exe`/`.com` targets directly through `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`; it does not perform shell interpolation.

Before the first run, the exact executable path + SHA-256 is shown for approval. Approval is hash-pinned, so replacing or modifying the binary requires approval again. Supported templates include `$input`, `$output`, `$width`, `$height`, `$ocrText`, `$windowTitle`, capture/source values and custom workflow/CLI variables. PNG or UTF-8 text output can be chained back into later workflow steps.

The workflow engine does not know about WinUI windows directly; side effects are provided through application host adapters/callbacks so the same pipeline model can be invoked from overlay, History, Watch or CLI paths.

### Magic Recipes · PRO

A `.magicrecipe` can mix deterministic steps and AI actions:

```text
STEP:RunOcr
STEP:ExtractSignals
AI:developer.bug-report
STEP:CopyText:markdown
```

Recipes are declarative and contain no executable scripts. AI does reasoning; deterministic application steps retain control of side effects.

Custom `.magicaction` definitions can also be referenced by recipes.

# Custom Destinations · PRO

A Pro user can configure user-owned HTTP destinations instead of relying on a Magic Capture upload service.

- GET / POST / PUT / PATCH;
- JSON or multipart image bodies;
- templated headers/query/body;
- PasswordVault-backed `{secret:id}` values;
- result URL extraction;
- bounded response size;
- HTTPS required for remote endpoints.

# Capture Watch

Capture Watch re-captures the last region on a bounded interval.

Free can run ordinary timed captures. Plus/Pro can use deterministic pixel change thresholds so the configured workflow only triggers after a meaningful change.

This prevents wasting disk/API/AI work on unchanged pixels.

# CLI / automation

The MSIX manifest exposes the `magiccapture.exe` execution alias.

Examples:

```powershell
magiccapture.exe --capture region
magiccapture.exe --capture monitor
magiccapture.exe --capture desktop
magiccapture.exe --workflow bug-report
magiccapture.exe --workflow my-local-pipeline --var project=demo --var quality=high
magiccapture.exe --open history
magiccapture.exe --open ai
```

A secondary CLI process forwards commands to the already-running resident instance rather than creating another tray host.

# Control Center

The Control Center remains secondary to `Win + Shift + X` and uses ordinary WinUI/Windows conventions.

Current navigation:

```text
Home
History
Workflows
Utilities
Destinations · PRO
Stitch · PLUS
Compare · PRO
AI & Magic · PRO
Settings
Upgrade / Plan
About
```

No decorative web-style dashboard is required for normal capture work.

# Architecture

```text
Resident tray process
       │
Win + Shift + X / CLI / Watch / History
       │
Capture Asset
       │
       ├──────── deterministic engines ────────┐
       │   OCR · Table · QR · Signals          │
       │   Imaging · Diff · Utilities          │
       │                                       ▼
       │                                  ScreenGraph
       │                                       │
       │                          ┌────────────┴─────────────┐
       │                          │                          │
       │                     no AI path                Pro AI path
       │                          │                          │
       │                    Workflow steps       Capability Router
       │                          │                Local / Cloud AI
       │                          │                          │
       └─────────────── Magic Recipe / Workflow ◄───────────┘
                                      │
                         Copy · Export · Editor · Pin
                         Destination · Result · Evidence
```

The solution is split into:

- `Magic.Capture.Core` — platform-neutral deterministic models/algorithms, commerce, ScreenGraph, AI contracts/router/action definitions, workflow/recipe/destination/CLI models and xUnit contracts.
- `Magic.Capture.App` — WinUI, Win32/WinRT capture, OCR/barcode/imaging, resident lifecycle, Store commerce, provider HTTP adapters, PasswordVault, UI, workflow side effects and MSIX packaging.

## Design documents

- [`docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-ai-intelligence-design.md`](docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-ai-intelligence-design.md)
- [`docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-design.md`](docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-design.md)
- [`docs/superpowers/plans/2026-08-23-magic-capture-desktop-v2-ai-intelligence-implementation.md`](docs/superpowers/plans/2026-08-23-magic-capture-desktop-v2-ai-intelligence-implementation.md)
- [`docs/superpowers/plans/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-implementation.md`](docs/superpowers/plans/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-implementation.md)

Other release documentation:

- [`docs/FEATURE_MATRIX.md`](docs/FEATURE_MATRIX.md)
- [`docs/AI_PROVIDER_GUIDE.md`](docs/AI_PROVIDER_GUIDE.md)
- [`docs/COMMERCIAL_MODEL.md`](docs/COMMERCIAL_MODEL.md)
- [`docs/SHAREX_CLEAN_ROOM.md`](docs/SHAREX_CLEAN_ROOM.md)
- [`packaging/STORE_SUBMISSION.md`](packaging/STORE_SUBMISSION.md)
- [`docs/WINDOWS_RELEASE_CHECKLIST.md`](docs/WINDOWS_RELEASE_CHECKLIST.md)

# Stack

- C# / .NET 10
- WinUI 3
- Windows App SDK modular WinUI + Runtime components
- Windows 10 build 19041 minimum
- MSIX / package identity
- x64 + ARM64
- Per-Monitor-V2 DPI awareness
- `Windows.Media.Ocr`
- `System.Drawing.Common`
- `Vortice.Direct3D11` + `Vortice.DXGI` 3.8.3 for isolated Direct3D/DXGI capture backends
- `ZXing.Net.Bindings.Windows.Compatibility`
- `HttpClient` + `System.Text.Json` in the isolated AI-provider/custom-destination layers
- Windows PasswordVault for provider/destination secrets
- local JSON + PNG persistence
- no mandatory backend

No OpenAI/Anthropic/Gemini SDK, Semantic Kernel, ONNX runtime or bundled generative model is required by the project.

# Build on Windows

Prerequisites:

1. Windows 10 2004+; Windows 11 recommended for development.
2. Visual Studio 2026 with Windows application development/WinUI tooling.
3. .NET 10 SDK matching `global.json`.

```powershell
.\scripts\test.ps1
.\scripts\build.ps1 -Configuration Release
```

For production Store packaging, associate the project with the real Partner Center identity first, then run:

```powershell
.\scripts\store-preflight.ps1
.\scripts\pack.ps1
```

# Static repository verification

```bash
python scripts/verify-repo.py
```

This checks repository structure, XML/XAML parseability, branding/version contracts, modular dependency rules, tier boundaries, AI/network/secret boundaries, endpoint policies, XAML handler wiring, required 2.0 architecture files and minimum core test-suite breadth.

It is **not** a substitute for Windows compilation/runtime testing.

# Clean-room feature competition

Magic Capture Desktop may study publicly observable workflow categories of mature screenshot tools, but production source must remain an independent implementation. See [`docs/SHAREX_CLEAN_ROOM.md`](docs/SHAREX_CLEAN_ROOM.md).
