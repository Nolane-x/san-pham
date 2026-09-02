# Magic Capture Desktop 2.0 Workflow Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend Magic Capture Desktop 2.0 with a clean-room ShareX-class composable workflow layer, deterministic utility pack, user-owned destinations, and stronger Pro AI orchestration without making AI part of the capture fast path.

**Architecture:** Add provider-neutral workflow/value contracts to `Magic.Capture.Core`; map them to existing application services in `Magic.Capture.App`. Deterministic steps run locally. Pro-only Magic Action steps call the existing ScreenGraph/provider runtime. User-owned HTTP destinations and AI providers use dedicated network layers with PasswordVault secret references and explicit endpoint safety rules.

**Tech Stack:** C#/.NET 10, WinUI 3, Windows App SDK/MSIX, System.Text.Json, System.Drawing for existing bitmap operations, HttpClient only in dedicated destination/AI provider layers, xUnit tests.

**Spec:** `docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-design.md`

## Global Constraints

- Product display name: `Magic Capture Desktop`.
- Free is permanent; Plus is a 168-hour trial only; Pro Lifetime is the only paid tier.
- AI is Pro-only.
- ShareX code is not copied; feature behavior is reimplemented clean-room.
- AI and uploads never run on `Win + Shift + X` capture start.
- Remote endpoints require HTTPS; local loopback endpoints may use HTTP.
- Secrets use Windows PasswordVault references, never plaintext JSON.
- No vendor AI SDK package dependencies.

---

### Task 1: Workflow core

**Files:**
- Create: `src/Magic.Capture.Core/Workflows/WorkflowModels.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowValidator.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowCatalog.cs`
- Test: `tests/Magic.Capture.Core.Tests/WorkflowTests.cs`

**Interfaces:**
- Produces `CaptureWorkflow`, `WorkflowStep`, `WorkflowStepKind`, `WorkflowValidationResult`, `WorkflowCatalog.BuiltIns`.

- [ ] Write failing workflow validation/catalog tests.
- [ ] Verify test source references types that do not yet exist.
- [ ] Implement typed immutable workflow contracts and validation.
- [ ] Add built-in Quick Copy, OCR, Documentation, Data Capture and Bug Report workflow definitions.
- [ ] Run repository verifier.

### Task 2: Utility core

**Files:**
- Create: `src/Magic.Capture.Core/Utilities/HashUtility.cs`
- Create: `src/Magic.Capture.Core/Utilities/ImageCombineLayout.cs`
- Create: `src/Magic.Capture.Core/Utilities/ImageSplitPlan.cs`
- Create: `src/Magic.Capture.Core/Utilities/BeautifyOptions.cs`
- Test: `tests/Magic.Capture.Core.Tests/UtilityCoreTests.cs`

**Interfaces:**
- Produces deterministic hash strings, combine placement math, split rectangles and validated beautify options.

- [ ] Write failing tests for hash output, combine placement and split planning.
- [ ] Verify production types do not yet exist.
- [ ] Implement deterministic utility contracts/math.
- [ ] Run verifier.

### Task 3: Utility application services

**Files:**
- Create: `src/Magic.Capture.App/Utilities/ImageUtilityService.cs`
- Create: `src/Magic.Capture.App/Utilities/MetadataService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- `ImageUtilityService.Combine`, `.Split`, `.Thumbnail`, `.Beautify`, `.StripMetadata`
- `MetadataService.Inspect`, `.ComputeHashes`

- [ ] Implement image combine/split/thumbnail/beautify using existing bitmap conventions.
- [ ] Implement metadata/hash inspection without AI/network.
- [ ] Register services.
- [ ] Run verifier.

### Task 4: Workflow persistence and execution

**Files:**
- Create: `src/Magic.Capture.App/Workflows/WorkflowStore.cs`
- Create: `src/Magic.Capture.App/Workflows/WorkflowExecutionContext.cs`
- Create: `src/Magic.Capture.App/Workflows/WorkflowExecutor.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- `WorkflowStore.LoadAsync/SaveAsync`
- `WorkflowExecutor.ExecuteAsync(CaptureWorkflow, WorkflowExecutionContext, CancellationToken)`

- [ ] Persist user workflow profiles as declarative JSON without secrets.
- [ ] Map deterministic steps to clipboard/export/OCR/table/barcode/pin/editor services.
- [ ] Map MagicAction step through existing Pro-only `MagicActionService`.
- [ ] Stop execution on failed required steps and return per-step results.
- [ ] Run verifier.

### Task 5: Custom destination core

**Files:**
- Create: `src/Magic.Capture.Core/Destinations/DestinationModels.cs`
- Create: `src/Magic.Capture.Core/Destinations/EndpointPolicy.cs`
- Create: `src/Magic.Capture.Core/Destinations/TemplateExpander.cs`
- Test: `tests/Magic.Capture.Core.Tests/DestinationTests.cs`

**Interfaces:**
- Produces `CustomHttpDestination`, safe endpoint classification and placeholder expansion.

- [ ] Write tests for HTTPS remote, HTTP localhost, blocked HTTP remote and placeholder expansion.
- [ ] Implement contracts and endpoint policy.
- [ ] Run verifier.

### Task 6: Custom HTTP destination application layer

**Files:**
- Create: `src/Magic.Capture.App/Destinations/DestinationProfileStore.cs`
- Create: `src/Magic.Capture.App/Destinations/CustomHttpDestinationClient.cs`
- Create: `src/Magic.Capture.App/Destinations/IDestinationSecretStore.cs`
- Create: `src/Magic.Capture.App/Destinations/WindowsDestinationSecretStore.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`

**Interfaces:**
- Dedicated network layer with bounded response size, timeout, JSON/multipart requests and secret references.

- [ ] Store destination profiles without plaintext secrets.
- [ ] Resolve secrets through PasswordVault.
- [ ] Enforce endpoint policy before request.
- [ ] Bound response body and timeout.
- [ ] Register services and preserve no-network capture fast path.

### Task 7: AI Guard and cache core

**Files:**
- Create: `src/Magic.Capture.Core/Ai/AiGuard.cs`
- Create: `src/Magic.Capture.Core/Ai/AiCacheKey.cs`
- Test: `tests/Magic.Capture.Core.Tests/AiGuardTests.cs`

**Interfaces:**
- `AiGuard.Scan(string)` returns deterministic findings.
- `AiCacheKey.Create(...)` returns stable SHA-256 cache key material.

- [ ] Write tests for bearer tokens, JWTs, private-key headers, emails and stable cache keys.
- [ ] Implement deterministic patterns conservatively.
- [ ] Run verifier.

### Task 8: AI cache and recipe orchestration

**Files:**
- Create: `src/Magic.Capture.App/Ai/AiResultCache.cs`
- Create: `src/Magic.Capture.Core/Ai/MagicRecipe.cs`
- Create: `src/Magic.Capture.Core/Ai/MagicRecipeValidator.cs`
- Test: `tests/Magic.Capture.Core.Tests/MagicRecipeTests.cs`
- Modify: `src/Magic.Capture.App/Ai/MagicActionService.cs`

**Interfaces:**
- Optional local AI result cache without secrets.
- Safe recipe chain that cannot invoke arbitrary shell execution.

- [ ] Add recipe validation tests.
- [ ] Implement recipe contracts/validator.
- [ ] Add optional cache read/write around explicit Magic Action execution.
- [ ] Keep provider selection and evidence contracts unchanged.

### Task 9: Feature gates and settings

**Files:**
- Modify: `src/Magic.Capture.Core/Commerce/ProductFeature.cs`
- Modify: `src/Magic.Capture.Core/Commerce/FeatureCatalog.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettings.cs`
- Test: `tests/Magic.Capture.Core.Tests/CommerceTests.cs`

**Interfaces:**
- Add workflow/utilities/destinations/AI recipe/cache feature gates with Free/Plus/Pro policy.

- [ ] Extend tests before feature catalog implementation.
- [ ] Preserve all AI and destinations as Pro-only where specified.
- [ ] Keep deterministic basic workflows usable in Free.

### Task 10: CLI core and primary-instance dispatch

**Files:**
- Create: `src/Magic.Capture.Core/Cli/CliCommand.cs`
- Create: `src/Magic.Capture.Core/Cli/CliParser.cs`
- Test: `tests/Magic.Capture.Core.Tests/CliParserTests.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Parse capture/workflow/open commands; redirect to primary instance without exposing secrets.

- [ ] Write parser tests.
- [ ] Implement parser.
- [ ] Wire supported commands after services initialize.
- [ ] Reject unknown/unsafe commands with user-visible error/log.

### Task 11: Workflow and utility Control Center UI

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Add Windows-native Workflows and Utilities navigation pages using existing WinUI controls.

- [ ] Expose built-in workflow profiles and enable/disable/save controls.
- [ ] Expose metadata/hash/image utility operations for selected History items.
- [ ] Keep capture interaction secondary to `Win + Shift + X`.

### Task 12: Capture overlay workflow integration

**Files:**
- Modify: `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/Capture/CaptureCoordinator.cs`

**Interfaces:**
- Overlay `More` menu exposes workflows without running analysis at capture start.

- [ ] Add workflow launcher after selection.
- [ ] Do not build ScreenGraph or call network until chosen workflow requires it.
- [ ] Keep existing Copy/Save/Pin/Text/Table/QR/Edit/Color/Magic actions intact.

### Task 13: Documentation and verifier

**Files:**
- Create: `docs/AI_PROVIDER_GUIDE.md`
- Create: `docs/RELEASE_NOTES_2.0.0.md`
- Create: `docs/COMPETITIVE_AUDIT_SHAREX.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/COMMERCIAL_MODEL.md`
- Modify: `README.md`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Verifier enforces clean-room/no-ShareX-source boundary, network layer boundaries, no plaintext secrets, AI Pro-only and required new files/tests.

- [ ] Document provider setup/local AI/privacy.
- [ ] Document clean-room competitive strategy and no GPL code copying.
- [ ] Update release notes and matrix.
- [ ] Upgrade verifier for workflow/destination/AI guard/cache contracts.
- [ ] Run verifier on working tree and clean export.

### Task 14: Release candidate packaging

**Files:**
- Modify: `release/version.json` only if release metadata requires correction.
- Use: `scripts/source-release.py`

**Interfaces:**
- Produce `Magic-Capture-Desktop-2.0.0-source.zip` and SHA-256.

- [ ] Run repository verifier.
- [ ] Parse XAML/XML/MSIX manifests.
- [ ] Create clean source export.
- [ ] Run verifier on clean export.
- [ ] Verify ZIP integrity and excluded artifacts.
- [ ] Generate SHA-256.
- [ ] State explicitly that Windows compilation/MSIX packaging is unverified unless Windows CI/Visual Studio has actually run.
