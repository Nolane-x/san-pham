# Magic Capture Desktop 2.0 — AI Provider Guide

Magic Capture Desktop 2.0 does not sell AI tokens, proxy requests through a Magic Capture cloud, or require a Magic Capture account. AI is a **Pro Lifetime** capability. The user supplies either an API credential for a supported provider or a local endpoint/model.

The capture utility remains deterministic-first: OCR, table reconstruction, QR/barcode decoding, color tools, stitching, image transforms, pixel diff, signal extraction, history and ordinary workflows do not require AI.

## 1. Supported provider families

The provider layer is capability-based rather than model-name-based.

| Provider family | Typical endpoint | Key required | Local | Vision possible | Notes |
|---|---|---:|---:|---:|---|
| OpenAI Responses | `https://api.openai.com` | Yes | No | Yes | Native Responses adapter |
| Anthropic Messages | `https://api.anthropic.com` | Yes | No | Yes | Native Messages adapter |
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta` | Yes | No | Yes | Native `generateContent` adapter |
| OpenRouter | `https://openrouter.ai/api/v1` | Yes | No | Model dependent | OpenAI-compatible transport |
| OpenAI-compatible | User supplied | Maybe | Maybe | Model dependent | vLLM/custom/company endpoints and compatible servers |
| Ollama | `http://localhost:11434` | No by default | Yes | Model dependent | Native Ollama `/api/chat` transport |
| LM Studio | `http://localhost:1234/v1` | No by default | Yes | Model dependent | OpenAI-compatible local endpoint |

Provider and model names evolve. The defaults are convenience values only; **capabilities and model discovery are the product contract**.

## 2. Secrets

API credentials are never fields in the JSON provider profile.

Magic Capture Desktop stores credentials through Windows `PasswordVault` via `WindowsPasswordVaultSecretStore`. A provider profile stores only a `SecretId` reference.

Do not put credentials in:

- `settings.json`;
- History metadata;
- `.magicaction` files;
- `.magicrecipe` files;
- logs;
- destination JSON profiles;
- exported ScreenGraph text.

If a provider does not need a credential, leave the secret empty.

## 3. Endpoint security

Remote AI endpoints must use HTTPS. Plain HTTP is permitted only for an endpoint that resolves as local/loopback, such as Ollama or LM Studio on `localhost`.

The same rule applies to Custom Destinations. This prevents a normal remote profile from silently transmitting screenshots, OCR or credentials over plaintext HTTP.

## 4. Model capability profiles

Each model profile declares capabilities instead of relying on model-name heuristics.

Current capability flags include:

- text input;
- vision input;
- multiple images;
- structured JSON;
- JSON Schema;
- streaming;
- tool calling;
- reasoning;
- local endpoint.

It also records:

- context size class: Small / Medium / Large;
- vision quality: None / Basic / Strong.

The router evaluates the Magic Action's minimum and preferred capabilities against these flags.

## 5. Routing modes

### Active only

Use only the selected active provider/model. If it cannot satisfy the action's required capabilities, the action fails instead of silently switching providers.

### Prefer local

Rank compatible local providers first, then choose the strongest compatible configured alternative. This is the recommended mode for users who want local models for routine work and a cloud model for harder vision/reasoning tasks.

### Best capability

Rank every compatible enabled provider by preferred capabilities, context class, vision quality and active-provider preference.

The cloud confirmation dialog is built from the **resolved provider**, not merely the provider selected in the settings UI.

## 6. Small-model strategy

A small local model should not be treated as a large vision model with fewer parameters.

Magic Capture Desktop first builds a ScreenGraph containing deterministic context such as:

- OCR text and geometry;
- tables;
- barcodes;
- URLs, emails, paths, stack frames and error signals;
- capture geometry;
- evidence node IDs.

For text-only models, the action can run on the compact ScreenGraph without sending image pixels.

For basic vision models, Magic Capture Desktop sends ScreenGraph context plus a downscaled relevant crop.

For strong vision models, the planner may include the primary image and relevant Context Stack images when the action benefits from them.

This allows inexpensive/local 3B–8B-class models to remain useful for OCR-heavy tasks while stronger multimodal models receive richer visual input.

## 7. Image preprocessing

AI image preprocessing is separate from the original capture and never changes evidence coordinates.

Typical strategy:

- basic vision: reduce the longest edge to roughly 1600 pixels when needed;
- strong vision: allow a larger image, roughly 2560 pixels on the longest edge when needed;
- text-only: send no image.

Evidence references use ScreenGraph node IDs rather than coordinates generated by the AI, so evidence remains anchored to the original capture.

## 8. Privacy controls

Pro settings include:

- prefer ScreenGraph/text when possible;
- never send images to cloud providers;
- local providers only;
- show payload summary before cloud action;
- routing mode selection.

A cloud Magic Action must be user initiated. Magic Capture Desktop shows the provider/model that will actually receive the request and summarizes image/context payloads before sending when confirmation is enabled.

A workflow or Magic Recipe is not allowed to bypass this boundary. Local AI can run directly; a cloud AI step must still go through the cloud-confirmation/AI-Guard path.

Capture Watch must not silently upload changed screen regions to a cloud AI provider in the background.

## 9. AI Guard

Before a cloud request, Magic Capture Desktop scans ScreenGraph text using deterministic rules for likely sensitive material, including:

- private-key headers;
- Bearer tokens;
- JWT-like tokens;
- API-key assignments;
- connection strings containing passwords;
- password assignments;
- emails;
- phone numbers;
- IP addresses.

Secret-like findings use redacted previews. The purpose is warning and informed consent, not a claim of complete DLP coverage.

AI Guard is deterministic and does not send the content to another model to decide whether the first model may see it.

## 10. Prompt-injection boundary

Text recognized from the screen is untrusted source data.

`MagicPromptCompiler` explicitly separates application instructions from captured text and instructs the model not to treat content inside the captured-data boundary as higher-priority instructions. This reduces the risk of a page or screenshot containing text such as “ignore previous instructions” altering the action contract.

This is defense-in-depth; model behavior must still be treated as untrusted output.

## 11. Structured output and evidence

Magic Actions are designed to return a structured result contract containing:

- title;
- Markdown/text body;
- optional fields;
- evidence node IDs.

Evidence IDs are namespaced:

- `p:*` — primary capture;
- `c1:*`, `c2:*`, ... — Context Stack captures.

The app resolves IDs to deterministic ScreenGraph geometry. AI-generated coordinates are not trusted as the canonical source.

## 12. Context Stack

A Pro user can collect several captures and run one action with a primary capture plus supporting context.

The planner avoids wasting image input:

- text-only model → compact ScreenGraphs only;
- single/basic vision model → limited relevant image set;
- strong multi-image model → primary plus compatible context images.

Context Stack is bounded to prevent unbounded token/image growth.

## 13. Result cache

`AiResultCache` keys results using the relevant capture/context/action/model/strategy identity. Repeating the same request can return the local cached result without spending another API call.

The UI should identify cache hits so the user understands that no provider request was made.

## 14. Magic Actions

Built-in actions cover categories such as:

- explain/summarize/translate/extract;
- error explanation and bug reports;
- code explanation/debugging/test ideas;
- table explanation/anomaly/trend analysis;
- UI description, UX/accessibility review and documentation;
- action-item/entity extraction;
- semantic comparison.

A custom `.magicaction` is declarative. It contains action metadata, instructions, capability requirements and output expectations; it is not executable code.

## 15. Magic Recipes

A `.magicrecipe` composes deterministic and AI steps, for example:

```text
OCR
→ Extract deterministic signals
→ Run "Create Bug Report" Magic Action
→ Copy Markdown
```

The AI performs the reasoning step; capture, OCR, export, editor, clipboard, destinations and other side effects remain explicit application steps.

## 16. Provider testing checklist

For every configured provider/model:

1. Save the profile without storing the plaintext key in profile JSON.
2. Test connection.
3. Discover models when the provider supports discovery.
4. Run a text-only action.
5. If declared vision-capable, run a one-image action.
6. If declared multi-image, run a Context Stack action.
7. If structured output is declared, verify a real structured result parses.
8. Verify an invalid/expired key produces a bounded, actionable error.
9. Verify response-size limits reject oversized responses.
10. Verify timeout/cancellation returns control to the UI.
11. Verify remote HTTP is rejected and localhost HTTP remains usable.
12. Verify local-only and never-send-cloud-image policies.

## 17. User cost model

Magic Capture Desktop Pro Lifetime licenses the integration/runtime, not AI usage.

- Cloud provider charges, quotas and retention policies belong to the user's chosen provider account.
- Local model compute uses the user's own hardware/runtime.
- Magic Capture Desktop does not resell inference or add a per-token fee.

The Plan/AI UI must make this distinction clear.
