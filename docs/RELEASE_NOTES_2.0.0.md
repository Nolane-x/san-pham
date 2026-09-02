# Magic Capture Desktop 2.0.0 — Release Notes

Magic Capture Desktop 2.0 is the architectural expansion from a capture utility into a deterministic-first **screen intelligence and capture workflow platform**.

The core interaction remains unchanged: `Win + Shift + X` freezes the active monitor, the user selects a region, and common actions remain immediately available. AI does not run on the capture fast path and remains a Pro Lifetime capability.

## Highlights

### ScreenGraph

2.0 introduces a structured ScreenGraph compiled from deterministic capture analysis. It can include OCR text/geometry, tables, barcodes, error/path/URL/email signals, capture geometry and evidence nodes.

This makes AI optional rather than foundational and lets small text models reason over already-cleaned context.

### Pro AI — BYOK/BYOM

Pro Lifetime adds provider/model integration without a Magic Capture inference service.

Supported provider families in source include OpenAI Responses, Anthropic Messages, Gemini, OpenRouter, generic OpenAI-compatible endpoints, Ollama and LM Studio.

API credentials use Windows PasswordVault. Local endpoints can operate without cloud credentials.

### Capability routing

Provider routing is based on declared capabilities, context size and vision quality rather than hard-coded assumptions about model brand names.

Routing modes include Active only, Prefer local and Best capability.

### Magic Actions

2.0 ships a catalog of context-aware actions for general text, developer errors/code, tables/data, UI/documentation and semantic comparison.

Users can create/import/export declarative `.magicaction` files.

### Evidence Anchoring

AI results can reference ScreenGraph evidence IDs. The UI resolves those IDs back to deterministic source geometry so claims/fields can be related to the original pixels.

Evidence is namespaced across primary and Context Stack captures.

### Context Stack

Pro can combine a primary capture with supporting captures. Text-only models receive compact ScreenGraphs; vision models receive only the images appropriate for their capabilities and the action.

### Semantic Compare

The existing deterministic pixel comparison remains intact. Pro can additionally run a Magic Action that describes semantic differences between two captures.

### Capture Pipeline / Workflows

2.0 adds declarative workflows inspired by the productivity value of mature capture tools while using an independent implementation.

Built-in flows include Quick Copy, OCR → Copy, Documentation, Data Capture and Bug Report.

Workflows can mix deterministic steps and Pro AI steps without letting the model control the entire application.

### Magic Recipes

Pro can compose richer deterministic + AI pipelines in declarative `.magicrecipe` files. Recipes can call built-in or custom Magic Actions.

### Custom Destinations

Pro adds user-owned custom HTTP destinations with templated request fields, PasswordVault-backed secrets, bounded responses and HTTPS-by-default endpoint policy. Magic Capture Desktop does not host a developer-operated upload relay.

### Utility Pack

2.0 adds deterministic utility services including image metadata, common hashes, metadata stripping, screenshot beautification, thumbnailing, image combine layouts and image splitting.

### Capture Watch

Capture Watch can recapture a region periodically. Advanced change-aware mode compares pixels first and triggers a workflow only when the change threshold is met. This keeps background monitoring deterministic and avoids unnecessary AI/API calls.

Cloud AI in a workflow still requires the cloud privacy confirmation path; Capture Watch must not silently transmit screen content to a cloud model.

### CLI and single-instance command bus

The MSIX package exposes the `magiccapture.exe` execution alias. CLI invocations can forward capture/open/workflow commands to the already-running tray process instead of spawning a second resident instance.

## AI safety/privacy hardening

- PasswordVault-backed provider secrets.
- HTTPS required for remote endpoints; HTTP restricted to actual localhost/loopback endpoints. Provider locality is determined from the configured endpoint, not merely from provider brand/type.
- Deterministic AI Guard for likely tokens/private keys/password material and common PII signals.
- Captured screen text is delimited as untrusted source data to reduce prompt-injection risk.
- Cloud payload confirmation uses the provider actually selected by the router.
- Provider responses are size-bounded.
- OpenAI Responses requests set `store=false` by default.
- AI result cache can avoid repeated provider calls for identical work. Cache identity includes compiled-prompt and prepared-image-payload hashes so OCR/prompt/image-strategy changes do not reuse stale results.

## What remains deterministic

2.0 does **not** replace the reliable capture engine with AI. The following continue to run locally without a generative model:

- screen/window/monitor capture;
- freeze-region selection;
- OCR;
- table reconstruction/serialization;
- QR/barcode recognition;
- annotation, transforms and color tools;
- scrolling/stitching;
- pixel comparison;
- text-signal extraction;
- History search;
- image utilities;
- ordinary workflow steps;
- change detection for Capture Watch.

## Tier model

- **Free** — free forever; complete everyday capture foundation.
- **Plus** — automatic 168-hour trial only; never sold and no AI.
- **Pro Lifetime** — the only paid tier; permanent Plus/power features plus all AI, provider, Magic Action, Context Stack, evidence, recipe and custom-destination capabilities.

Commercial baseline remains US $29.99 MSRP for Pro Lifetime with the planned US $19.99 launch price for the first 90 consecutive days of public Pro availability. Store-localized pricing is shown by the app rather than hard-coded price text.

## Verification status

This source bundle is generated in a Linux container that does not contain .NET/Visual Studio/Windows SDK. Repository/static checks can be run here, but successful WinUI compilation, xUnit execution, MSIX packaging and Windows runtime behavior remain mandatory Windows release gates.
