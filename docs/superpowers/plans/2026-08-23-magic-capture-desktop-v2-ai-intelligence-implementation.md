# Magic Capture Desktop 2.0 AI Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic-first Screen Intelligence Runtime and Pro-only BYOK/BYOM AI system without adding AI overhead to Free/Plus capture paths.

**Architecture:** Existing capture/analysis remains the source of truth. A new provider-neutral ScreenGraph is compiled lazily from deterministic outputs. Magic Actions use a capability router and context planner to send the smallest sufficient context to user-configured cloud/local providers, then resolve evidence IDs back to source pixels.

**Tech Stack:** C# / .NET 10, WinUI 3, Windows App SDK, `HttpClient`, Windows PasswordVault, System.Text.Json, xUnit, MSIX.

**Spec:** `docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-ai-intelligence-design.md`

## Global Constraints

- Product name is `Magic Capture Desktop`.
- AI is Pro Lifetime only; Plus trial does not unlock AI.
- No AI model loads or runs automatically after capture.
- User supplies API key or local endpoint/model.
- Never persist API keys in JSON/log/history.
- Deterministic engines run first.
- Windows-native build verification is external to this Linux environment.

---

### Task 1: Version and product feature contract

**Files:**
- Modify `release/version.json`
- Modify `Directory.Build.props`
- Modify `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify `src/Magic.Capture.App/Package.appxmanifest`
- Modify `src/Magic.Capture.Core/Commerce/ProductFeature.cs`
- Modify `src/Magic.Capture.Core/Commerce/FeatureCatalog.cs`
- Test `tests/Magic.Capture.Core.Tests/CommerceTests.cs`

**Produces:** `ProductFeature.AiProviders`, `MagicActions`, `ContextStack`, `EvidenceAnchoring`, `SemanticCompare`, `CustomMagicActions`, all Pro-only.

- [ ] Add failing tier tests proving Plus cannot use any AI feature.
- [ ] Update feature contract.
- [ ] Synchronize version to 2.0.0 / 2.0.0.0.
- [ ] Run repository verifier.

### Task 2: Deterministic ScreenGraph core

**Files:**
- Create `src/Magic.Capture.Core/ScreenGraph/*`
- Create `src/Magic.Capture.Core/Signals/*`
- Test `tests/Magic.Capture.Core.Tests/ScreenGraphTests.cs`
- Test `tests/Magic.Capture.Core.Tests/TextSignalExtractorTests.cs`

**Produces:** stable graph/node/evidence IDs and deterministic signal extraction.

- [ ] Write tests for stable OCR node IDs/bounds.
- [ ] Write tests for URL/email/file/error/stack-frame extraction.
- [ ] Implement ScreenGraph models and builder.
- [ ] Implement deterministic signal extractor/error parser.

### Task 3: AI capability and request planning core

**Files:**
- Create `src/Magic.Capture.Core/Ai/AiCapabilities.cs`
- Create `src/Magic.Capture.Core/Ai/AiModelProfile.cs`
- Create `src/Magic.Capture.Core/Ai/AiContextPlanner.cs`
- Create `src/Magic.Capture.Core/Ai/AiRequestModels.cs`
- Test `tests/Magic.Capture.Core.Tests/AiContextPlannerTests.cs`

**Produces:** `AiContextPlan Plan(...)`.

- [ ] Test text-only routes zero images.
- [ ] Test basic vision routes current capture only.
- [ ] Test strong vision may route context stack images.
- [ ] Implement planner and payload summary.

### Task 4: Magic Action runtime definitions

**Files:**
- Create `src/Magic.Capture.Core/Ai/MagicActionDefinition.cs`
- Create `src/Magic.Capture.Core/Ai/BuiltInMagicActions.cs`
- Create `src/Magic.Capture.Core/Ai/MagicActionValidator.cs`
- Create `src/Magic.Capture.Core/Ai/MagicPromptCompiler.cs`
- Test `tests/Magic.Capture.Core.Tests/MagicActionTests.cs`
- Test `tests/Magic.Capture.Core.Tests/MagicPromptCompilerTests.cs`

**Produces:** built-in catalog and validated custom action schema.

- [ ] Test catalog IDs are unique.
- [ ] Test capability requirements.
- [ ] Test unsafe custom fields are rejected.
- [ ] Implement prompt compiler with evidence contract.

### Task 5: Evidence resolver and context stack

**Files:**
- Create `src/Magic.Capture.Core/Ai/AiActionResult.cs`
- Create `src/Magic.Capture.Core/Ai/EvidenceResolver.cs`
- Create `src/Magic.Capture.Core/Ai/ContextStack.cs`
- Test `tests/Magic.Capture.Core.Tests/EvidenceResolverTests.cs`
- Test `tests/Magic.Capture.Core.Tests/ContextStackTests.cs`

**Produces:** evidence-to-pixel mapping and ordered max-8 stack.

- [ ] Test unknown evidence IDs are ignored.
- [ ] Test multiple node bounds are returned.
- [ ] Test stack order/remove/limit.
- [ ] Implement immutable core models.

### Task 6: Provider configuration and secret storage

**Files:**
- Create `src/Magic.Capture.App/Ai/Provider/AiProviderProfile.cs`
- Create `src/Magic.Capture.App/Ai/Provider/AiProviderProfileStore.cs`
- Create `src/Magic.Capture.App/Ai/Provider/IAiSecretStore.cs`
- Create `src/Magic.Capture.App/Ai/Provider/WindowsPasswordVaultSecretStore.cs`
- Create `src/Magic.Capture.App/Ai/Provider/AiProviderRegistry.cs`

**Produces:** non-secret JSON profiles + credential-vault secrets.

- [ ] Implement profile persistence without credential field.
- [ ] Implement PasswordVault adapter.
- [ ] Ensure log/debug representations redact secret IDs/values.

### Task 7: HTTP provider clients

**Files:**
- Create `src/Magic.Capture.App/Ai/Provider/IAiProviderClient.cs`
- Create `src/Magic.Capture.App/Ai/Provider/AiProviderRequest.cs`
- Create `src/Magic.Capture.App/Ai/Provider/AiProviderResponse.cs`
- Create adapters for OpenAI, Anthropic, Gemini, OpenAI-compatible and Ollama.
- Create `AiProviderClientFactory.cs`

**Produces:** one common `GenerateAsync` contract.

- [ ] Build provider-specific HTTP request payloads without SDKs.
- [ ] Add timeout/cancellation and sanitized errors.
- [ ] Support image attachment only when context plan includes it.
- [ ] Support JSON response request where provider supports it.

### Task 8: ScreenGraph application bridge

**Files:**
- Create `src/Magic.Capture.App/Ai/ScreenGraphService.cs`
- Modify deterministic analysis as needed to expose signals.
- Modify `ApplicationServices.cs`.

**Produces:** lazy `BuildAsync(CaptureAsset)` graph generation.

- [ ] Reuse OCR/table/barcode outputs.
- [ ] Add process/window/source metadata available from capture.
- [ ] Cache only per-open result/session, not global background analysis.

### Task 9: Magic execution service

**Files:**
- Create `src/Magic.Capture.App/Ai/MagicActionService.cs`
- Create `src/Magic.Capture.App/Ai/AiResponseParser.cs`
- Create `src/Magic.Capture.App/Ai/AiPrivacyPolicy.cs`
- Modify `ApplicationServices.cs`.

**Produces:** end-to-end action execution with provider selection, plan, prompt, response validation and evidence resolution.

- [ ] Gate all executions via Pro entitlement.
- [ ] Fail cleanly with no configured provider.
- [ ] Respect local-only / never-send-images settings.
- [ ] Never log prompt or response.

### Task 10: Custom action persistence

**Files:**
- Create `src/Magic.Capture.App/Ai/MagicActionStore.cs`
- Create import/export DTO validation.

**Produces:** local action storage and `.magicaction` import/export.

- [ ] Bound file size.
- [ ] Reject unsupported schema versions.
- [ ] No executable/script fields.

### Task 11: AI result workspace

**Files:**
- Create `Views/MagicActionWindow.xaml`
- Create `Views/MagicActionWindow.xaml.cs`

**Produces:** action picker/result/evidence highlight/copy/export UI.

- [ ] Provider/model badge.
- [ ] Built-in/custom action picker.
- [ ] user question field for general ask.
- [ ] cancellation.
- [ ] evidence list with source highlight.
- [ ] copy Markdown/plain/JSON.

### Task 12: Overlay integration

**Files:**
- Modify `CaptureOverlayWindow.xaml(.cs)`
- Modify `CaptureCoordinator.cs` and action enum/model as necessary.

**Produces:** `Magic · PRO` action without capture-path AI work.

- [ ] Free/Plus route to Plan page.
- [ ] Pro opens Magic Action workspace.
- [ ] no ScreenGraph build before click.

### Task 13: Control Center AI & Magic page

**Files:**
- Modify `MainWindow.xaml(.cs)`

**Produces:** Windows-native provider/action configuration UI.

- [ ] Add provider profile.
- [ ] cloud/local badge.
- [ ] model/endpoint/API-key input.
- [ ] PasswordVault save.
- [ ] connection test.
- [ ] privacy toggles.
- [ ] custom action list/import/export.

### Task 14: Semantic compare and context stack UI

**Files:**
- Modify `CompareWindow.xaml(.cs)`
- Add minimal context-stack management UI.

**Produces:** deterministic pixel compare remains; Pro can send two captures to Semantic Compare.

- [ ] No AI changes deterministic compare behavior.
- [ ] Semantic Compare requires Pro/provider.

### Task 15: Verifier, docs, release packaging

**Files:**
- Modify `scripts/verify-repo.py`
- Modify `docs/FEATURE_MATRIX.md`
- Modify `docs/COMMERCIAL_MODEL.md`
- Create `docs/AI_PROVIDER_GUIDE.md`
- Create `docs/RELEASE_NOTES_2.0.0.md`
- Update `README.md`

- [ ] Assert no plaintext API key properties in settings/profile JSON DTOs.
- [ ] Assert AI features are Pro-only.
- [ ] Assert Magic overlay/button and AI navigation exist.
- [ ] Assert version sync 2.0.0.
- [ ] Run verifier on source tree and clean export.
- [ ] Generate integrity-checked source ZIP + SHA-256.
