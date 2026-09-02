# Magic Capture Desktop 2.0 — Screen Intelligence & Pro AI Architecture

**Status:** Approved for implementation  
**Product:** Magic Capture Desktop  
**Target:** Windows 10 2004+ / Windows 11, x64 + ARM64, MSIX  
**Release target:** 2.0.0  
**Product principle:** deterministic-first, AI-optional, Pro-only, BYOK/BYOM, local-first.

## 1. Product thesis

Magic Capture Desktop must not compete with ShareX by trying to own the longest checklist of screenshot utilities. Its differentiation is the work that happens *after* the pixels are captured.

The product promise is:

> **Capture it. Turn it into something useful.**

The base application remains a fast Windows capture utility. Its Pro moat is a Screen Intelligence Runtime that compiles pixels and deterministic extraction into a structured ScreenGraph, then routes only the necessary context to a user-selected AI provider or local model.

The model is replaceable. The moat is the capture context, deterministic preprocessing, ScreenGraph, Magic Action runtime, evidence anchoring, context stack, provider abstraction and post-processing pipeline.

## 2. Non-negotiable constraints

1. `Win + Shift + X` remains the primary interaction.
2. No AI model loads on app startup.
3. No AI request runs automatically after capture.
4. Free and Plus continue to work with no AI configured.
5. AI features are Pro Lifetime only. Plus trial never unlocks AI.
6. The user supplies API credentials or a local endpoint/model.
7. Magic Capture Desktop does not sell tokens and does not proxy model traffic through a Magic Capture server.
8. API keys must never be stored in JSON, history metadata, logs, crash reports or capture files.
9. Deterministic engines run before AI whenever they can solve or simplify a task.
10. Text-only and small local models must remain useful without native vision.
11. Cloud image transmission only occurs after an explicit Magic Action initiated by the user.
12. Never send pixels outside the selected capture/context items.
13. AI cannot perform destructive OS actions in 2.0. It may propose output; deterministic code performs user-confirmed copy/export/annotation.
14. Existing capture, OCR, table, QR, editor, pin, stitch, compare, history, tray and Store behavior must remain functional.
15. UI remains Windows-native and utility-oriented, not a web dashboard.

## 3. Tier contract

### Free

Core screenshot utility:
- region/window/monitor/desktop capture
- freeze overlay
- copy/save
- local OCR
- basic pin/editor/history/color

### Plus — 7-day trial only

Plus is never sold. It adds deterministic power features:
- table extraction and structured exports
- QR/barcode
- scrolling stitch
- advanced editor tools
- unlimited pins during trial
- advanced image export
- direct recognition actions

### Pro Lifetime

All Free + Plus features permanently, plus:
- repeat last region
- fixed aspect capture
- compare workspace
- click-through pins
- unlimited history controls
- **all AI integrations**
- ScreenGraph AI context
- provider profiles
- local AI endpoints
- Magic Actions
- custom Magic Actions
- evidence anchoring
- context stack
- semantic compare
- AI result workspace

AI capability is represented by explicit `ProductFeature` values and must be gated centrally through `FeatureCatalog`.

## 4. Architectural overview

```text
Win+Shift+X
    |
Freeze + select
    |
CaptureAsset
    |
+---------------- Deterministic pipeline ----------------+
| OCR | Table | Barcode | URLs | Errors | Code | Color  |
+-------------------------+------------------------------+
                          |
                     ScreenGraph
                          |
                 (no AI required yet)
                          |
                    Magic Action
                          |
                  Capability Router
             +------------+------------+
             |            |            |
         text-only    weak vision   strong vision
             |            |            |
         compact SG   SG + crop     SG + images
             +------------+------------+
                          |
                    Provider Client
                          |
                  Structured response
                          |
            Evidence resolver/postprocess
                          |
               Copy / Export / Annotate
```

## 5. Deterministic-first analysis

### 5.1 Existing engines retained
- Windows OCR
- Table reconstruction
- ZXing barcode/QR
- Pixel/color sampling
- Image compare
- Vertical stitch
- Annotation renderer
- Image transforms

### 5.2 New deterministic analyzers
These are intentionally non-AI:

#### Text signal extraction
Extract:
- URLs
- email addresses
- phone-like values
- IP addresses
- file paths
- line/column references
- monetary values
- percentages
- dates when unambiguous

#### Error parser
Recognize common error structures using text/regex heuristics:
- exception/error headline
- message
- stack-trace lines
- file path
- line/column
- error code

#### Code signal detector
Heuristics only; not language understanding:
- code-like density
- indentation structure
- brackets/operators
- likely language hints from keywords/extensions

These enrich ScreenGraph and reduce AI burden. They must never claim certainty beyond the confidence attached to each signal.

## 6. ScreenGraph

ScreenGraph is a provider-independent structured representation of a capture.

### 6.1 Document metadata
- schema version
- capture ID
- capture timestamp
- source kind
- source display name
- process name when available
- window title when available
- capture pixel bounds
- image width/height
- DPI when available
- OCR language if known

### 6.2 Nodes
Every meaningful element gets a stable ID such as `n1`, `n2`, `w14`, `t3`.

Node types include:
- document
- region
- text line
- OCR word
- table
- table cell
- barcode
- URL
- email
- error
- stack frame
- code region
- numeric signal
- generic deterministic signal

Each node may include:
- `id`
- `kind`
- `text`
- `bounds`
- `confidence`
- `attributes`
- `parentId`
- `children`

### 6.3 Evidence IDs
ScreenGraph node IDs are the citation language between model output and source pixels.

AI prompts request evidence IDs instead of invented coordinates:

```json
{
  "summary": "...",
  "evidence": ["w12", "n4"]
}
```

The app resolves IDs to pixel bounds. Models never need to calculate screen coordinates.

## 7. ScreenGraph compiler

`ScreenGraphBuilder` consumes:
- `CaptureAsset` metadata projected into a provider-neutral descriptor
- `OcrDocument`
- optional `DetectedTable`
- barcodes
- deterministic signals

The compiler must be deterministic and unit-testable in `Magic.Capture.Core`.

For table cells that cannot be geometrically mapped with high confidence, evidence may point to the table bounds rather than fabricate per-cell bounds.

## 8. AI provider abstraction

### 8.1 Provider types
Ship adapters for:
- OpenAI Responses API
- Anthropic Messages API
- Google Gemini API
- OpenRouter via OpenAI-compatible API
- generic OpenAI-compatible endpoint
- Ollama native API
- LM Studio through OpenAI-compatible endpoint

No provider SDK dependency is required in 2.0; use `HttpClient` and small explicit JSON DTOs to keep package size and update surface low.

### 8.2 Provider profile
A profile contains only non-secret fields:
- profile ID
- display name
- provider kind
- base URI
- model ID
- enabled
- capability overrides
- timeout
- image policy
- max context estimate

Secrets are referenced by a secret ID, never stored inline.

### 8.3 Secret storage
Windows implementation uses `Windows.Security.Credentials.PasswordVault` (or a future credential-manager abstraction). The abstraction is `IAiSecretStore`.

Required behaviors:
- save/replace credential
- retrieve credential only at request time
- delete credential
- never expose credential in `ToString()` or logs

Local endpoints may have no credential.

## 9. Capability model

Provider/model capability is represented as flags, independent from provider name:
- TextInput
- VisionInput
- MultipleImages
- StructuredJson
- JsonSchema
- Streaming
- ToolCalling
- Reasoning
- LocalEndpoint

An `AiModelProfile` includes:
- capabilities
- context class (`Small`, `Medium`, `Large`)
- vision quality (`None`, `Basic`, `Strong`)
- user overrides

### 9.1 Small-model strategy
For small/text-only local models:
1. deterministic extraction first
2. send compact ScreenGraph text
3. omit raw image
4. omit low-confidence/irrelevant nodes
5. use action-specific prompt
6. demand a small JSON response
7. use deterministic parser/postprocessor

### 9.2 Strong-model strategy
For strong vision models:
- send compact ScreenGraph
- send selected screenshot or evidence crops
- send multiple context images only when action benefits
- keep the same output contract and evidence IDs

## 10. Context budgeting

`AiContextPlanner` decides what enters the request.

Inputs:
- action definition
- model capability profile
- current ScreenGraph
- optional context stack graphs
- image dimensions

Outputs:
- compact ScreenGraph text/JSON
- selected image attachments
- image downscale targets
- omitted node reasons
- estimated payload summary

Rules:
- text-only model: zero images
- basic vision: current capture only, downscaled if large
- strong vision: current + required context images
- do not send history automatically
- context stack is explicit
- default max context-stack items: 8

## 11. Magic Actions

A Magic Action is declarative, not hard-coded UI behavior.

Definition fields:
- ID
- name
- description
- category
- system instruction
- user instruction template
- minimum capabilities
- preferred capabilities
- whether vision is useful/required
- whether multiple captures are useful
- output kind
- output JSON schema description
- evidence requirement
- built-in/custom

### 11.1 Built-in actions
Ship a broad built-in catalog.

#### General
- Explain capture
- Summarize
- Translate
- Extract key facts
- Turn into clean notes
- Ask a custom question

#### Errors / developer
- Explain error
- Create bug report
- Extract stack trace
- Suggest likely causes
- Suggest debugging checklist
- Explain code
- Find likely bug in code screenshot
- Generate test ideas

#### Tables/data
- Explain table
- Clean/normalize interpretation
- Find anomalies
- Describe trends
- Extract structured records

#### UI/product
- Describe UI
- UX review
- Accessibility review
- Generate UI documentation
- Turn UI into acceptance criteria

#### Documents
- Summarize document fragment
- Extract action items
- Extract entities/fields
- Convert to structured note

#### Visual compare
- Semantic compare (requires two context items)

## 12. Custom Magic Actions

Pro users may create reusable actions locally.

Stored as JSON under app data and exportable as `.magicaction`.

Custom action supports:
- title/description/category
- prompt template
- required/preferred capability flags
- output type: Markdown, plain text, JSON
- optional JSON field schema
- evidence required toggle
- vision required/useful toggle
- context-stack requirement

No arbitrary executable scripts in 2.0. Import validates size, schema version and known fields.

## 13. Prompt compiler

Prompts are generated from:
1. invariant safety/integrity rules
2. action instruction
3. output contract
4. compact ScreenGraph
5. user question when applicable
6. evidence ID rules

Invariant requirements:
- do not claim source evidence that is absent
- evidence must use provided node IDs
- if uncertain, mark uncertainty
- do not invent exact values from charts unless visible/extracted
- output valid JSON when structured mode is requested

For providers without reliable structured-output support, the app requests fenced JSON and uses tolerant extraction with validation.

## 14. Evidence anchoring

AI response contract includes:
- result text/fields
- evidence IDs
- confidence/uncertainty when applicable

`EvidenceResolver` maps IDs to ScreenGraph nodes and merged pixel rectangles.

UI behaviors:
- evidence chips such as `Source 1`
- hover/click highlights source region on image
- unresolved evidence IDs are ignored and flagged internally
- never display fabricated bounds

## 15. Context Stack

A Context Stack is an explicit, local collection of captures for one AI task.

Operations:
- add current capture
- add from History
- remove/reorder
- label items
- clear stack
- max 8 items by default

Use cases:
- error + source + config
- requirement + implementation
- before + after
- multiple pages of one problem

No context item is sent until the user runs a Magic Action.

## 16. Semantic Compare

Existing deterministic compare remains unchanged and useful without AI.

Pro AI layer adds Semantic Compare:
- inputs: exactly two captures by default
- deterministic pixel stats included in ScreenGraph context
- AI describes meaningful content/UI changes
- evidence should point to relevant nodes in each capture where possible

The UI must clearly separate `Pixel Difference` from `Semantic Difference`.

## 17. AI result workspace

A lightweight Pro result surface, not a chatbot product.

Sections:
- action name
- provider/model badge
- result (Markdown/text/structured fields)
- evidence chips
- source image with highlight overlay
- Copy
- Copy Markdown
- Export JSON when structured
- Save result locally (optional, explicit)
- Run another action

No permanent conversational memory by default.

## 18. Overlay UX

After selection, primary deterministic commands remain first:
- Copy
- Save
- Pin
- Text
- Table
- QR
- Edit
- Color

Add one clear secondary primary command:
- `Magic · PRO`

Free/Plus users can see it and open the Pro explanation.
Pro users open an action flyout/window. AI never delays capture completion.

## 19. Control Center UX

Add an `AI & Magic` navigation page for Pro configuration:
- provider profiles
- add provider
- test connection
- model ID
- capability overrides
- local endpoint status
- cloud/local badge
- privacy payload policy
- Magic Action management
- custom action import/export

Free/Plus can see a read-only explanation and Upgrade to Pro.

Do not turn Home into an AI dashboard.

## 20. Provider implementations

### 20.1 OpenAI
Use Responses API with text plus optional `input_image` data URL. Parse output text and structured JSON contract. API key via Bearer header.

### 20.2 Anthropic
Use Messages API; content blocks may include base64 image blocks plus text. Required headers are isolated in adapter.

### 20.3 Gemini
Use Gemini REST content API with text parts and optional inline image bytes. Adapter is isolated so endpoint migration does not affect runtime interfaces.

### 20.4 OpenAI-compatible / OpenRouter / LM Studio
Use `/v1/chat/completions` baseline because it is widely implemented. Support text content and image data URLs where profile says VisionInput.

### 20.5 Ollama
Use native `/api/chat` with `stream:false`, optional base64 images and `format` JSON/schema when supported. Model list may be discovered from local Ollama API.

## 21. Request reliability

Every request has:
- cancellation
- configurable timeout
- max one automatic retry for transient HTTP 408/429/5xx when safe
- no retry for authentication/invalid request
- response size guard
- sanitized error message

Provider exceptions must not include API key or full Authorization header.

## 22. Privacy & payload transparency

Before first cloud AI call per provider, show a concise notice:
- selected screenshot may be sent to provider when action needs vision
- extracted text/ScreenGraph may be sent
- Magic Capture Desktop does not proxy the request
- provider terms/data controls apply

Settings expose:
- `Prefer text-only context when possible`
- `Never send images to cloud`
- `Local providers only`
- `Show payload summary before each cloud action`

Per-action payload summary:
- provider/model
- local/cloud
- number of images
- whether OCR text is included
- number of context items

## 23. Data persistence

Persist locally:
- provider profile non-secret configuration
- custom actions
- context-stack metadata referencing local history/capture files
- optional saved AI results only on explicit save

Do not persist by default:
- prompts with sensitive content
- AI responses
- API keys
- request payload images beyond existing capture/history data

## 24. Logging

Allowed:
- provider kind
- model ID
- latency
- status code
- action ID
- context item count
- image count

Forbidden:
- Authorization header
- API key
- raw prompt
- OCR text
- AI result
- screenshot bytes

## 25. New deterministic tools added in 2.0

To ensure the release is stronger even without AI:
- Text Signals panel: URLs/emails/paths/line refs/numbers
- Error structure extraction
- code-like capture detection
- quick Copy URL / Copy Email / Copy Error
- capture hash/info for developer diagnostics

These run in Free/Plus when appropriate and are reused by ScreenGraph.

## 26. Performance targets

Idle:
- no provider polling except optional local test initiated by user
- no local model process spawned by Magic Capture Desktop
- zero AI image preprocessing until Magic Action

Capture:
- Magic button must not change capture fast-path latency materially
- ScreenGraph may be built lazily when Magic is opened

AI:
- cancellation immediately available
- image downscaling happens off UI thread
- result rendering incremental only where provider supports it; streaming is optional for 2.0

## 27. Failure behavior

No provider configured:
- Pro Magic explains how to add provider/local endpoint.

Provider unavailable:
- deterministic capture remains intact
- user gets concise retry/configuration error

Model lacks vision:
- route through ScreenGraph text strategy instead of failing when action permits it.

Action truly requires vision:
- explain capability mismatch and suggest a vision-capable profile.

Invalid structured result:
- attempt tolerant JSON extraction once
- if still invalid, show raw response with `Could not validate structured result`
- never silently invent fields

## 28. Testing strategy

### Core unit tests
- ScreenGraph construction and stable evidence IDs
- deterministic signal extraction
- capability routing
- context planner image decisions
- prompt compiler includes/excludes correct context
- action catalog requirements
- custom action validation
- evidence resolver
- context stack ordering/limits
- AI feature tier gating

### Adapter tests
Use request-builder tests against `HttpRequestMessage`/JSON payload generation. Do not require real API keys in CI.

### Windows/manual tests
- PasswordVault persistence
- provider test connection
- local Ollama/LM Studio
- OpenAI/Gemini/Anthropic real smoke tests with user-owned test keys
- cloud-image privacy notice
- Magic overlay flow
- evidence highlight
- Free/Plus cannot execute AI

## 29. Definition of done — 2.0 source

The source release is ready for Windows release validation when:
1. deterministic existing verifier is green
2. new ScreenGraph/core AI tests are present
3. all AI `ProductFeature` flags require Pro Lifetime
4. provider abstraction and at least OpenAI-compatible, OpenAI, Gemini and Ollama adapters are implemented; Anthropic included in architecture and source adapter
5. secrets use Windows credential store abstraction
6. no secret value appears in JSON settings/logging code
7. overlay exposes `Magic · PRO`
8. Control Center has provider/action management UI
9. built-in Magic Action catalog ships
10. custom actions can be stored/imported/exported safely
11. context stack model and management exists
12. evidence IDs resolve to pixel regions
13. no AI code executes for capture unless explicitly invoked
14. version/package/docs are synchronized at 2.0.0
15. Windows build/test/MSIX remains a required external gate and is never falsely claimed from Linux static verification.
