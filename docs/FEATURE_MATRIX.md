# Magic Capture Desktop 4.16.0 — Feature Matrix

This matrix maps the current commercial/product capabilities to implementation and release verification. `Static` means repository/source contract verification only; Windows-native behavior still requires the Windows release checklist.

## 4.16 Work Recovery note

4.16 extends the crash-safe recovery architecture from Annotation to Documentation Builder and Video Editor. `.magicdoc` and `.magicclip` work now receive bounded revision snapshots plus atomic journal promotion, per-window 1.5-second debounce, generation-token race protection, Home Recover/Discard entry points, and safe recovered-copy semantics that do not retain or overwrite the original full project path. Future-schema video projects remain read-only and are excluded from autosave. Current ledger: **464 Done / 46 Partial / 92 Foundation / 36 Missing / 22 ReleaseTest = 660**.

| Recovery capability | Implementation | Verification |
|---|---|---|
| Shared bounded workspace-recovery journal policy | `Core/Recovery/WorkspaceRecoveryPolicy.cs` | `WorkspaceRecoveryPolicyTests.cs`, `verify-work-recovery.py` |
| Annotation `.magiccapture` recovery | `EditableProjectRecoveryStore.cs`, `AnnotationWindow.xaml.cs` | existing 4.10 recovery checklist + source gates |
| Documentation `.magicdoc` recovery | `DocumentationRecoveryStore.cs`, `DocumentationWindow.xaml.cs` | source gate + 4.16 Windows crash/relaunch tests |
| Video Editor `.magicclip` recovery | `VideoEditRecoveryStore.cs`, `VideoEditorWindow.xaml.cs` | source gate + 4.16 Windows crash/relaunch tests |
| Home recovery discovery / Recover / Discard | `MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml.cs` | XAML compile + Windows UI smoke |

## Core capture and resident lifecycle

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 1 | `Win + Shift + X` freeze-region capture | Free | `Platform/HotkeyService.cs`, `Capture/CaptureCoordinator.cs`, `Views/CaptureOverlayWindow.*` | Static + Windows hotkey/capture |
| 2 | Active-monitor freeze surface | Free | `Capture/CaptureCoordinator.cs`, overlay | Windows smoke |
| 3 | Foreground-window capture | Free | `Capture/WindowCaptureService.cs` | Windows smoke |
| 4 | Active-monitor capture | Free | `Capture/ScreenCaptureService.cs`, `MonitorService.cs` | Windows smoke |
| 5 | Virtual-desktop capture | Free | capture/monitor services | Mixed-DPI Windows smoke |
| 6 | Cursor capture option | Free | capture/settings | Windows smoke |
| 7 | Delayed capture | Free | overlay/coordinator/settings | Windows smoke |
| 8 | Repeat last region (`Win + Shift + R`) | Pro | `Core/Capture/LastRegionState.cs`, hotkey/coordinator | Core contract + Windows hotkey |
| 9 | Fixed 1:1 / 16:9 / 4:3 region | Pro | `Core/Geometry/AspectLockedSelection.cs`, overlay | Geometry tests + UI smoke |
| 10 | Per-Monitor-V2 physical geometry | Free | `app.manifest` | Static + mixed-DPI Windows smoke |
| 11 | Tray-resident lifecycle | Free | `TrayIconService.cs`, `App.xaml.cs` | Windows close-to-tray/Exit |
| 12 | Hidden startup activation | Free | `StartupService.cs`, MSIX startup task | Windows sign-in smoke |
| 13 | Single resident instance | Free | `SingleInstanceService.cs` | Windows repeated-launch test |
| 14 | CLI command forwarding to resident process | Plus / Pro | `Core/Cli/*`, `SingleInstanceService.cs`, `App.xaml.cs` | Core parser + Windows CLI smoke |
| 15 | MSIX execution alias `magiccapture.exe` | Plus / Pro | `Package.appxmanifest` | Manifest static + Windows CLI |

## Deterministic recognition and data extraction

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 16 | Local Windows OCR | Free | `Analysis/WindowsOcrService.cs` | Core resize contract + packaged Windows OCR |
| 17 | OCR geometry retained | Free | `Core/Ocr/*` | Core tests |
| 18 | Oversized OCR downscale + coordinate remap | Free | `OcrResizePlan.cs`, Windows OCR adapter | Core tests + large-image smoke |
| 19 | Text signals: URL/email/path/error/stack/etc. | Free | `Core/Signals/TextSignalExtractor.cs` | `TextSignalExtractorTests.cs` |
| 20 | Table reconstruction from OCR geometry | Plus / Pro | `Core/Tables/TableExtractor.cs` | Table tests + real capture |
| 21 | CSV table serialization | Plus / Pro | `TableSerializers.cs` | Serializer tests |
| 22 | TSV table serialization | Plus / Pro | `TableSerializers.cs` | Serializer tests |
| 23 | Markdown table serialization | Plus / Pro | `TableSerializers.cs` | Serializer tests |
| 24 | HTML table serialization | Plus / Pro | `TableSerializers.cs` | Serializer tests |
| 25 | JSON table serialization | Plus / Pro | `TableSerializers.cs` | Serializer tests |
| 26 | QR/barcode recognition | Plus / Pro | `Analysis/BarcodeService.cs` | Windows real-image smoke |
| 27 | Direct recognition actions on overlay | Plus / Pro | overlay/result UI | Windows smoke |

## Imaging, editing, pinning and comparison

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 28 | Copy image | Free | `ClipboardService.cs` | Windows clipboard |
| 29 | PNG/JPEG save | Free | `ExportService.cs`, `BitmapCodec.cs` | Windows save smoke |
| 30 | BMP/TIFF advanced export | Plus / Pro | export/codec | Windows format smoke |
| 31 | Basic annotation | Free | `AnnotationWindow.*`, `AnnotationRenderer.cs` | Model tests + Windows editor |
| 32 | Crop/resize/rotate/flip | Free | editor + `ImageTransformService.cs` | Windows editor |
| 33 | Highlight/blur/pixelate | Plus / Pro | editor/renderer | Windows editor |
| 34 | Color sampling | Free | `Core/Color/*` | Core tests |
| 35 | Pin window | Free | `PinWindow.*` | Windows pin smoke |
| 36 | Unlimited pins | Plus / Pro | entitlement/pin lifecycle | Windows tier smoke |
| 37 | Click-through pin + tray recovery | Pro | `PinWindow.*`, tray | Windows recoverability test |
| 38 | Pin opacity/resize/aspect preservation | Free | `PinWindow.*`, `AspectRatioResize.cs` | Core + Windows |
| 39 | Vertical image stitching | Plus / Pro | `VerticalImageStitcher.cs`, overlap matcher | Core overlap + Windows |
| 40 | Compare side-by-side | Pro | `CompareWindow.*` | Windows smoke |
| 41 | Compare overlay slider | Pro | `CompareWindow.*` | Windows smoke |
| 42 | Deterministic pixel-difference image | Pro | `ImageCompareService.cs` | Windows/fixture test |
| 43 | Changed-pixel percentage / mean difference | Pro | `ImageCompareService.cs` | Windows/fixture test |

## History and utilities

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 44 | Local History | Free | `Persistence/HistoryStore.cs` | Retention tests + Windows |
| 45 | History retention planner | Free/Pro unlimited options | `Core/History/*` | Core tests |
| 46 | Instant History search | Free | `HistorySearch.cs`, Control Center | Core tests + UI smoke |
| 47 | Metadata inspection | Free | `Utilities/MetadataService.cs` | Windows fixture smoke |
| 48 | SHA-256/SHA-1/MD5 helpers | Free | `Core/Utilities/HashUtility.cs` | Core tests |
| 49 | Basic screenshot beautify | Free | `ImageUtilityService.cs`, `BeautifyOptions.cs` | Windows fixture smoke |
| 50 | Advanced image utility pack | Plus / Pro | `ImageUtilityService.cs` | Windows fixture smoke |
| 51 | Strip metadata | Plus / Pro workflow/utility | utility/workflow engine | Windows fixture smoke |
| 52 | Image combine horizontal/vertical/grid | Plus / Pro | `ImageUtilityService.cs`, `ImageCombineLayout.cs` | Core contract + Windows |
| 53 | Image split plan | Plus / Pro | `ImageSplitPlan.cs`, utility service | Core contract + Windows |

## History Intelligence & Organization — 4.14.0

| Audit # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 258 | Collections (many-to-many, capture-safe delete) | Free | `Core/History/HistoryLibrary.cs`, `Persistence/HistoryLibraryStore.cs`, Library manager | Core/source contract + Windows UI smoke |
| 259 | Workspaces | Free | History library model/store + History filters | Core/source contract + Windows UI smoke |
| 260 | One-level folders under workspaces | Free | History library model/store + Library manager | Core/source contract + Windows UI smoke |
| 272 | Filter by actually-attempted Magic Action id | Free view / workflow tier to execute | `HistoryQuery.cs`, workflow activity instrumentation | Source contract + Windows workflow/filter gate |
| 273 | Filter by workflow id | Free view / workflow tier to execute | `HistoryQuery.cs`, workflow activity instrumentation | Source contract + Windows workflow/filter gate |
| 275 | Sort History by Most used | Free | bounded activity counters + `HistoryQuery` | Core/source contract + Windows UI smoke |
| 280 | Drag/drop History import and organizer assignment | Free | `MainWindow.*`, Library manager | Source contract + Windows drag/drop smoke |
| 285 | Chronological Timeline view | Free | `MainWindow.*` | Source contract + Windows selection/view smoke |
| 286 | Best-effort app/process icon metadata | Free | `HistoryProcessIconCache.cs`, `HistoryDisplayItem.cs` | Source contract + Windows icon extraction gate |
| 287 | Executable/process metadata preserved through History | Free | capture/window services, `HistoryItem`, History import/reload | Source contract + Windows window-capture gate |
| 451 | Drag image into History | Free | `History_DragOver`, `History_Drop` | Source contract + Windows shell drag/drop |
| 454 | Drag folder into History/import (top-level only, max 500) | Free | bounded drop enumeration/import | Source contract + Windows large-folder smoke |

Organizer/activity state is stored separately in atomic `history-library.json` (32 MiB hard read/write bound) so a corrupt or unavailable organizer file cannot redefine the authoritative History capture index. Activity stores identifiers/counts/timestamps only; no screenshot pixels, OCR/AI output, prompt answers or HTTP/Local Action payloads are persisted there.


## Settings & Personalization Runtime — 4.15.0

| Audit # | Capability | Implementation | Verification |
|---:|---|---|---|
| 588 | Custom hotkey for capture modes/profiles | `PersonalHotkeyBinding`, `HotkeyService`, capture/profile dispatch | Core/source contract + Windows RegisterHotKey smoke |
| 589 | Custom workflow hotkey | runtime workflow-id validation + dispatch | Source contract + Windows workflow smoke |
| 590 | Custom Magic Action hotkey | built-in/custom action-id validation + capture dispatch | Source contract + provider/local-action smoke |
| 591 | Custom editor hotkey | open latest History capture in Annotation editor | Source contract + Windows History/editor smoke |
| 596 | Reset individual Settings section | `AppSettingsRules.ResetSection`, seven Settings reset actions | Core/source contract + persistence smoke |
| 597–600 | Toolbar/overlay reorder and hide | normalized action allowlists + runtime WinUI layout | Source/structure contract + Windows interaction smoke |
| 601–603 | Default/last tool and multiple saved styles | Annotation runtime + schema-v2 presets | Core/source contract + Windows editor smoke |
| 604 | Per-monitor preferences | monitor-name cursor/post-action override at capture time | Source contract + mixed-monitor smoke |
| 605 | Per-app capture rule | executable-name → capture-profile rule at Region/Foreground dispatch | Source contract + Windows foreground-app smoke |

Personalization is bounded configuration. The settings file stores identifiers, gestures, style numbers, device names and executable file names only; it does not contain pixels, OCR/AI text, clipboard contents or workflow result payloads.

## Capture pipelines, automation and destinations

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 54 | Declarative workflow model | Free | `Core/Workflows/*` | Workflow tests |
| 55 | Quick Copy workflow | Free | `WorkflowCatalog.cs` | Workflow tests + overlay smoke |
| 56 | OCR → Copy workflow | Free | `WorkflowCatalog.cs` | Workflow tests + Windows |
| 57 | Documentation workflow | Plus / Pro | workflow executor + editor | Windows |
| 58 | Data Capture workflow | Plus / Pro | workflow executor/table | Windows |
| 59 | Bug Report hybrid workflow | Pro | workflow executor + Magic Action | Windows/provider test |
| 60 | Workflow host adapters keep UI outside core | Architecture | `WorkflowExecutor.cs`, `WorkflowExecutionContext.cs` | Static review |
| 61 | Auto Capture / timed Capture Watch | Free | `CaptureWatchService.cs` | Windows timer smoke |
| 62 | Change-aware Watch threshold | Plus / Pro | `CaptureWatchService.cs`, pixel compare | Windows change fixture |
| 63 | Watch triggers selected workflow | Tier of workflow enforced | Watch + workflow executor | Windows smoke |
| 64 | Custom HTTP destinations | Pro | `Core/Destinations/*`, `App/Destinations/*` | Core tests + endpoint fixture |
| 65 | GET/POST/PUT/PATCH destinations | Pro | destination models/client | Core validation + fixture |
| 66 | JSON/multipart destination body | Pro | custom destination client | fixture |
| 67 | Templated header/query/body | Pro | `TemplateExpander.cs` | core tests |
| 68 | PasswordVault destination secrets | Pro | `WindowsDestinationSecretStore.cs` | Windows PasswordVault smoke |
| 69 | Remote HTTPS / loopback HTTP destination policy | Pro | `EndpointPolicy.cs` | `DestinationTests.cs` |
| 70 | Bounded destination response | Pro | `CustomHttpDestinationClient.cs` | static + fixture |

## Automation Triggers — 4.12.0

| Audit # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 438 | Windows Task Scheduler integration | Plus / Pro | `WindowsTaskSchedulerService.cs`, `CliParser.cs`, `App.xaml.cs` | Source contract + Windows Task Scheduler gate |
| 439 | Schedule workflow locally | Plus / Pro | `WorkflowTriggerRunner.cs`, `WorkflowTriggerStore.cs`, Trigger Manager | Source contract + Windows scheduled-run gate |
| 440 | File watcher trigger | Plus / Pro | `ResidentWorkflowTriggerEngine.cs`, `FileSystemWatcher` | Source contract + Windows create/change/rename storm gate |
| 441 | Clipboard trigger | Plus / Pro | `ResidentWorkflowTriggerEngine.cs`, `AddClipboardFormatListener` | Source contract + Windows clipboard notification/privacy gate |
| 442 | Target-window-change trigger | Plus / Pro | `ResidentWorkflowTriggerEngine.cs`, `SetWinEventHook` | Source contract + Windows foreground switch gate |
| 443 | Process-start trigger | Plus / Pro | `ResidentWorkflowTriggerEngine.cs`, bounded `PeriodicTimer` | Source contract + Windows process lifecycle gate |
| 444 | Hotkey trigger | Plus / Pro | `WorkflowTriggerHotkeyService.cs`, `RegisterHotKey` | Source contract + collision/reload/entitlement gate |

`WorkflowTriggerPolicy` caps configuration at 64 triggers / 16 workflow hotkeys and rejects interactive capture profiles. `WorkflowTriggerRunner` serializes accepted attempts, re-checks `AdvancedWorkflows` + workflow tier + current capture profile, applies cooldown from completion and a 20-runs/5-minute circuit breaker, then records metadata-only newest-200 history best-effort. `ResidentWorkflowTriggerEngine` bounds event storms to one pending event per trigger and tears down all resident sources when entitlement is lost or the app exits. No Windows service, cloud scheduler or background network dependency is introduced.


## Workflow Control Flow & Safe Resume — 4.13.0

| Capability | Tier | Implementation | Verification |
|---|---|---|---|
| Bounded `ForEachImage` child-workflow loop over selected History images (max 32) | Plus / Pro | `WorkflowRuntimePolicy.cs`, `WorkflowExecutor.cs`, Workflows History UI | Core/source contract + Windows multi-image loop gate |
| Loop variables `loop.index`, `loop.number`, `loop.count`; nested-loop collapse; no whole-loop retry | Plus / Pro | `WorkflowExecutor.cs`, `WorkflowValidator.cs` | Core tests + failure/continue/cancel smoke |
| SHA-256 workflow execution-contract fingerprint in payload-free trace metadata | Plus / Pro | `WorkflowFingerprint.cs`, `WorkflowTraceStore.cs` | Core fingerprint tests + trace privacy inspection |
| Safe failed-workflow replay from original local History capture | Plus / Pro | `WorkflowResumePlanner.cs`, resume context in executor, trace UI | Source contract + Windows resume/reject matrix |
| Repeated-resume cumulative safe-side-effect suppression | Plus / Pro | trace `ResumeCompletedSideEffectStepIds`, resume planner/executor | Source contract + Windows two-failure replay gate |

## Workflow Runtime v4 — 4.11.0

| Capability | Tier | Implementation | Verification |
|---|---|---|---|
| Typed workflow parameters (`Text`, `Choice`, `Boolean`) with bounded resolution/preflight | Plus / Pro | `WorkflowModels.cs`, `WorkflowParameterResolver.cs`, `WorkflowValidator.cs`, Workflow Studio | Core/source contract + Windows interaction |
| Interactive `PromptText`, Prompt Choice and Confirm steps plus bounded Delay | Plus / Pro | `WorkflowExecutor.cs`, `WorkflowExecutionContext.cs`, `App.xaml.cs` dialogs | Source contract + Windows dialog/cancel/timeout gate |
| Reusable `RunWorkflow` subworkflow with depth-4/cycle rejection | Plus / Pro, child tier still enforced | `WorkflowRuntimePolicy.cs`, `WorkflowExecutor.cs`, `WorkflowStore.cs` | Core/source contract + Windows nested-workflow gate |
| Sequential History batch workflow, hard 500-capture cap, lazy asset loading | Plus / Pro | `WorkflowBatchRunner.cs`, `MainWindow.xaml.cs` | Source contract + Windows 1/500/cancel/failure gate |
| No-side-effect dry-run on one History capture | Plus / Pro | `WorkflowRuntimePolicy.cs`, `WorkflowExecutor.cs`, Studio `WorkflowDryRun_Click` | Source contract + Windows side-effect assertion |
| Newest-100 privacy-safe execution trace + step logs, including payload-free preflight failures | Plus / Pro | `WorkflowTraceStore.cs`, `App.xaml.cs`, Studio trace UI | Source contract + trace privacy inspection |

## Step Recorder & Documentation Builder

| Capability | Tier | Implementation | Verification |
|---|---|---|---|
| Explicit session-scoped Step Recorder; no resident hook during tray idle | Plus / Pro (`AdvancedWorkflows`) | `Documentation/StepRecorderInputTracker.cs`, `StepRecorderService.cs`, `DocumentationWindow.*` | Source contract + Windows hook lifecycle gate |
| Mouse clicks + safe shortcut labels only; no printable-text buffer; password-target suppression | Plus / Pro | input tracker + UI Automation snapshot + `DocumentationPolicy` | Core/source contract + Windows privacy gate |
| UIA-aware bounded crop, automatic title/description/step number and click marker | Plus / Pro | `DocumentationPolicy.cs`, `DocumentationCardRenderer.cs` | Core policy + Windows mixed-DPI/UIA gate |
| Add/remove, move, duplicate, merge, step title/section/description editing | Plus / Pro | `DocumentationWindow.xaml(.cs)` | XAML structural gate + Windows interaction smoke |
| Bounded `.magicdoc` save/reopen with traversal, duplicate, future-schema and size rejection | Plus / Pro | `DocumentationProjectStore.cs`, `DocumentationArchivePolicy.cs` | Core/source contracts + Windows round-trip/malformed fixtures |
| long PNG / PDF / DOCX / HTML / Markdown + images / self-contained offline HTML | Plus / Pro | `DocumentationExportService.cs`, `DocumentationTextExport.cs`, `DocumentationDocxWriter.cs` | Source contracts + independent Windows viewer/browser fixtures |
| Documentation Publishing: native drag reorder with Move Up/Down fallback | Plus / Pro | `DocumentationWindow.xaml(.cs)` (`CanReorderItems`, `TemplateComboBox`) | XAML/source contract + Windows drag/keyboard interaction gate |
| Clean / Compact / Presentation / Print page templates | Plus / Pro | `DocumentationTemplateCatalog.cs`, card/DOCX/text exporters | Core/source contract + Windows/viewer fidelity gate |
| Authored header/footer + optional embedded project logo | Plus / Pro | `DocumentationWindow.*`, `DocumentationProjectStore.cs`, export/render writers | Source contract + `.magicdoc` round-trip + viewer gate |
| generated table of contents with stable step anchors | Plus / Pro | `DocumentationTextExport.BuildContents`, overview renderer, DOCX writer | Core/source contract + browser/Word/PDF viewer gate |

## ScreenGraph and deterministic AI context

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 71 | ScreenGraph document | Pro AI path | `Core/ScreenGraph/*` | ScreenGraph tests |
| 72 | OCR nodes with stable evidence IDs | Pro AI path | ScreenGraph builder | tests |
| 73 | Deterministic signal nodes | Pro AI path | builder + signal extractor | tests |
| 74 | Table/barcode context in graph | Pro AI path | `ScreenGraphBuilder.cs` | tests |
| 75 | Evidence source rectangles | Pro AI path | ScreenGraph models/resolver | tests |
| 76 | Primary/context evidence namespacing | Pro | `EvidenceResolver.cs`, Context Stack | evidence tests |

## Pro AI provider runtime

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 77 | AI providers are Pro-only | Pro | `FeatureCatalog.cs`, UI gates | commerce/static |
| 78 | OpenAI Responses adapter | Pro | `OpenAiResponsesClient.cs` | Windows real provider test |
| 79 | Anthropic Messages adapter | Pro | `AnthropicMessagesClient.cs` | Windows real provider test |
| 80 | Gemini adapter | Pro | `GeminiClient.cs` | Windows real provider test |
| 81 | OpenRouter / generic OpenAI-compatible | Pro | `OpenAiCompatibleClient.cs` | Windows endpoint test |
| 82 | Ollama native adapter | Pro | `OllamaClient.cs` | Windows local model test |
| 83 | LM Studio via OpenAI-compatible endpoint | Pro | provider factory/profile | Windows local model test |
| 84 | Model discovery | Pro | provider clients + AI settings | real endpoints |
| 85 | PasswordVault provider credentials | Pro | `WindowsPasswordVaultSecretStore.cs` | Windows credential smoke |
| 86 | Remote AI HTTPS / loopback HTTP policy | Pro | `AiEndpointPolicy.cs`, provider base/UI | `AiEndpointPolicyTests.cs` + Windows |
| 87 | Provider response size bound | Pro | `AiProviderClientBase.ReadJsonAsync` | provider fixture |
| 88 | Timeout / limited retry | Pro | provider base | provider fixture |
| 89 | OpenAI `store=false` default | Pro | native OpenAI adapter | request inspection fixture |
| 90 | Active-only routing | Pro | `AiProviderRouter.cs` | router tests |
| 91 | Prefer-local routing | Pro | router | router tests |
| 92 | Best-capability routing | Pro | router | router tests |
| 93 | Small/medium/large context class | Pro | capability models/planner | planner tests |
| 94 | None/basic/strong vision profile | Pro | capability models/planner | planner tests |
| 95 | Text-only small-model path | Pro | `AiContextPlanner.cs` | planner tests |
| 96 | Vision image downscale | Pro | `AiImagePreprocessor.cs` | Windows image fixture |
| 97 | Never-send-cloud-images policy | Pro | privacy/planner | planner tests + provider smoke |
| 98 | Local-providers-only policy | Pro | privacy/router | tests + UI |
| 99 | Cloud payload confirmation | Pro | `MagicActionWindow` / workflow confirmation path | Windows UI test |

## Magic Actions, evidence and recipes

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 100 | Built-in Magic Action catalog | Pro | `BuiltInMagicActions.cs` | action tests |
| 101 | Deterministic action recommendations | Pro | `MagicActionRecommender.cs` | recommender tests |
| 102 | Structured Magic Action result | Pro | `AiActionResult.cs`, parser | parser tests |
| 103 | Evidence anchoring | Pro | resolver + Magic UI | evidence tests + Windows highlight |
| 104 | Context Stack | Pro | `ContextStack.cs`, app context service | context tests + Windows |
| 105 | Semantic Compare | Pro | `compare.semantic` + Compare UI | Windows real model test |
| 106 | Custom `.magicaction` | Pro | `MagicActionStore.cs`, validators/UI | validator + Windows import/export |
| 107 | Declarative `.magicrecipe` | Pro | recipe core/store/service/UI | recipe tests + Windows |
| 108 | Recipe calls built-in/custom Magic Actions | Pro | `MagicRecipeService.cs`, workflow executor | Windows integration |
| 109 | AI result cache | Pro | `AiCacheKey.cs`, `AiResultCache.cs` | cache tests + Windows |
| 110 | AI Guard | Pro | `AiGuard.cs` | `AiGuardTests.cs` |
| 111 | Secret previews redacted | Pro | `AiGuard.RedactedPreview` | tests |
| 112 | Captured text treated as untrusted prompt data | Pro | `MagicPromptCompiler.cs` | prompt tests |
| 113 | Workflow cloud AI cannot bypass confirmation | Pro | workflow/Magic action execution path | Windows integration |
| 114 | Capture Watch cannot silently send cloud AI | Pro safety | Watch + workflow confirmation contract | Windows integration |

## 2.1 stability-first desktop foundations

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 130 | Saved capture profiles with exact region, cursor, delay, action, workflow and save format | Free / tier of workflow | `Core/Capture/CaptureProfiles.cs`, Control Center, `App.xaml.cs` | Core normalization + Windows profile smoke |
| 131 | Recent capture regions and exact X/Y/W/H capture | Free | `CaptureProfiles.cs`, Control Center | Core + mixed-DPI Windows smoke |
| 132 | Automatic vertical/horizontal scrolling + bounded 2D grid capture | Plus / Pro | `AutomaticScrollCaptureService.cs`, `TwoDimensionalScrollCaptureService.cs`, horizontal/vertical/grid stitchers, input synthesis | Static + long-page / horizontal / 2D Windows smoke |
| 133 | Editable `.magiccapture` project packages + crash-safe local autosave recovery | Free | `Core/Projects/EditableProject*.cs`, `EditableProjectService.cs`, `EditableProjectRecoveryStore.cs`, editor + Home recovery card | Core policy tests + Windows save/reopen/crash recovery |
| 134 | Non-destructive annotation layer identity/z-order/visibility/lock/rotation | Free | `AnnotationDocumentEditor.cs`, editor layer sidebar | Core tests + Windows editor |
| 135 | Local Smart Redact plan from OCR/ScreenGraph evidence | Plus / Pro | `Core/Privacy/*`, editor | Core detector + Windows OCR fixture |
| 136 | Workflow conditions/retry/timeout with cancellation correctness | Tier of workflow | `WorkflowConditionEvaluator.cs`, `WorkflowExecutor.cs` | Core tests + Windows workflow smoke |
| 137 | Rich History metadata: title/notes/tags/favorite/source/session fields | Free | `Core/History/*`, `HistoryStore.cs` | Core search + Windows metadata edit |
| 138 | History corruption recovery + safe path containment | Free | `AtomicJsonFile.cs`, `HistoryStore.cs`, `LocalPathGuard.cs` | Core path tests + corruption fixture |
| 139 | UI Automation-ready ScreenGraph node schema | Pro AI path | `ScreenGraphModels.cs`, `ScreenGraphBuilder.cs` | Core merge tests; native UIA extractor planned |
| 140 | Compare MSE/PSNR/SSIM-style metrics | Pro | `ImageComparisonMetrics.cs`, Compare UI | Core metrics + Windows fixture |
| 141 | LockBits pixelate/box-blur hot path | Plus / Pro | `PixelBufferEffects.cs`, renderer | Static + Windows image fixtures |
| 142 | Lightweight Capture Watch sampled change detection | Free / Plus / Pro | `FrameDifference.cs`, `CaptureWatchService.cs` | Core + Windows timer smoke |
| 143 | Bounded content-aware ScreenGraph cache | Pro AI path | `ScreenGraphService.cs` | Static + resident soak test |
| 144 | Runtime settings normalization boundary | All | `AppSettingsRules.cs`, `SettingsStore.cs`, `ApplicationServices.cs` | Core tests + verifier |
| 145 | Bounded current-user-only single-instance IPC | All | `SingleInstanceService.cs` | Static + Windows hostile-payload smoke |
| 146 | Editable-project malformed-data limits | All project users | `EditableProjectValidator` | Core validation + malformed package fixture |

## 2.2 power-UX deterministic capabilities

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 147 | Editor multi-select, group/ungroup and batch layer operations | Free | `AnnotationDocumentEditor.cs`, `AnnotationWindow.*` | Core tests + Windows editor smoke |
| 148 | Editor alignment, distribution, equal-size, copy/paste and editable style/bounds | Free | `AnnotationDocumentEditor.cs`, `AnnotationWindow.*`, `AnnotationRenderer.cs` | Core tests + Windows editor smoke |
| 149 | Compare threshold/transparent policy, RGB metrics, heatmap, mask, blink and triptych | Pro | `ImageDifference.cs`, `ImageCompareService.cs`, `CompareWindow.*` | Core tests + Windows rendering smoke |
| 150 | Bounded translation auto-alignment for Compare | Pro | `TranslationAlignment.cs`, Compare service/UI | Core tests + Windows fixture |
| 151 | Local outbound privacy policies for Copy/Save/Pin/Workflow | Plus / Pro where OCR/redaction is used | `SensitiveDataDetector.cs`, `CaptureRedactionService.cs`, `App.xaml.cs` | Core tests + Windows outbound-path smoke |
| 152 | User sensitive words + bounded custom regex redaction rules | Plus / Pro | privacy/settings core + Control Center | Core normalization/detector tests + Windows settings smoke |
| 153 | Capture precision HUD, post-drag resize handles, reselect and persisted dark/light overlay | Free | `SelectionHandleMath.cs`, `CaptureOverlayWindow.*`, settings | Core geometry + mixed-DPI Windows smoke |
| 154 | Exact 1..660 feature ledger with evidence/status counts | Release engineering | `docs/FEATURE_AUDIT_660.md`, `release/feature-audit-660.json` | verifier exact-ID/count contract |

## Commerce and packaging

| # | Capability | Tier | Implementation | Verification |
|---:|---|---|---|---|
| 115 | Free forever | Commerce | `Core/Commerce/*` | commerce tests |
| 116 | Plus exactly 168 hours | Commerce | `TrialClock.cs`, `TrialStateStore.cs` | commerce tests |
| 117 | Clock rollback guard | Commerce | `TrialClock.cs` | commerce tests |
| 118 | Plus has no AI | Commerce | `FeatureCatalog.cs` | verifier |
| 119 | Pro Lifetime unlock | Commerce | Store/entitlement services | Store flight |
| 120 | Cached confirmed Pro on temporary Store failure | Commerce | Store/entitlement services | Store flight offline |
| 121 | Localized Store price | Commerce | Store purchase service / Plan UI | Store flight |
| 122 | MSIX / full trust | Packaging | project + manifest | verifier + Windows pack |
| 123 | x64 + ARM64 | Packaging | solution/scripts | Windows CI/package |
| 124 | Modular Windows App SDK references | Packaging | app project | verifier |
| 125 | No bundled AI SDK/model runtime | Packaging | project dependency policy | verifier |
| 126 | Version synchronization | Packaging | `release/version.json`, project, manifest | verifier |
| 127 | Store identity fail-fast | Packaging | `store-preflight.ps1` | Windows associated build |
| 128 | Deterministic source ZIP + SHA-256 | Packaging | `source-release.py` | source-release gate |
| 129 | Clean-room no ShareX production-source dependency | Architecture | `docs/SHAREX_CLEAN_ROOM.md`, verifier | static |

## Commercial contract

```text
Free                 forever
Plus                 first 168 hours only; never sold; no AI
Pro Lifetime          Microsoft Store Durable / Forever add-on
US MSRP               $29.99
US launch             $19.99 for 90 consecutive days
Subscription          none
Automatic charge      none
AI billing            user's provider/local compute, not Magic Capture
```

## Release truth rule

A row marked implemented/static is not a claim that the Windows runtime has been exercised in this Linux source-generation environment. Before public Store submission, complete `docs/WINDOWS_RELEASE_CHECKLIST.md` on Windows using the Store-associated package identity and real Free/Plus/Pro/provider states.

## 2.3 Library + Pin additions

| Area | Added source capability |
|---|---|
| History | Advanced metadata filters + deterministic sort |
| History | Session IDs, batch delete/tag/export, local image import |
| Pin | Zoom/Fit/1:1/Reset, Copy/Save/Edit, persisted opacity |
| Capture | Exact-region 720p/1080p/1440p/4K/social presets |
| Reliability | Locked-file-safe delete/clear/retention and duplicate-ID index recovery |

## 2.4.0 capture-engine additions

| Capability | Implementation | Validation |
|---|---|---|
| Sticky header/footer removal | `StableEdgeBandDetector`, `VerticalImageStitcher` frame trims | Core tests + 2.4 verifier contract; Windows fixture still required |
| Dynamic-content protection | bounded settle probe/retry in `AutomaticScrollCaptureService` | static contract; real animated-page fixture required |
| Scroll alignment correction | pair preflight + bounded reverse-wheel correction | static contract; Windows SendInput fixture required |
| Window menu / multi-window | bounded on-demand `WindowCaptureService` catalog | static contract; real window lifecycle fixture required |
| Monitor menu | on-demand `MonitorService.ListMonitors` | static contract; multi-monitor fixture required |
| Selection loupe | shared frozen image + transform, no re-encode on pointer move | XAML handler/static contract |
| Window snapping | `CaptureSnapRules.SelectSmallestContaining` | Core test + overlay contract |
| Capture-origin History fields | process/window/monitor metadata | Core query tests + static contract |

## 2.5.0 output, optimization and local utilities

| Capability | Implementation | Validation |
|---|---|---|
| PDF / multi-page / contact sheet | `PdfImageDocumentWriter`, `PdfExportService`, Utilities UI | Core deterministic writer tests + static contracts; Windows PDF-reader fixture required |
| Clipboard Data URI/Base64/file/path/folder | `ClipboardService`, Utilities UI | structural/static contracts; representative Windows paste targets required |
| JPEG target-size optimization | `ImageOptimizationPolicy`, `ImageOptimizationService` | Core policy tests + static contracts; Windows image fixtures required |
| PNG lossless/lossy + resize/batch | `ImageOptimizationService`, Utilities UI | static contracts; Windows pixel/codec fixtures required |
| QR / Code 128 generation | `BarcodeGeneratorService` | static contract; independent decoder fixture required |
| Directory/Clipboard/Window/Monitor utilities | Control Center Utilities + `MonitorTestWindow` | no-resident-worker architecture + Windows smoke |
| Pixel statistics | `Core/Imaging/PixelStatistics`, app decode adapter | Core tests + on-demand UI path |
| External editor launcher | explicit `.exe` picker + `ProcessStartInfo.ArgumentList` | static safety contract + Windows launch smoke |
| Structural release gate | `scripts/verify-structure.py` + `source-release.py` | executable in generation environment |

## 2.6.0 image-effect pipeline

| Capability | Implementation | Validation |
|---|---|---|
| Ordered bounded effect pipeline | `Core/Imaging/ImageEffectPipeline.cs`, `ImageEffectPipelineService.cs` | Core normalization tests + source contracts; Windows pixel fixtures required |
| Brightness/contrast/gamma/exposure/saturation | single BGRA buffer pass per ordered step | source contracts + Windows reference images |
| Grayscale/sepia/invert/posterize/threshold | deterministic pixel transforms | source contracts + Windows reference images |
| Built-in presets | `ImageEffectPresets.BuiltIn` | Core bounded-preset test |
| Batch effects | Utilities multi-selection path, max 500 | structural/source contracts; Windows batch smoke |

## 2.7.0 non-destructive editor tools

| Capability | Implementation | Validation |
|---|---|---|
| Speech balloon + callout | annotation kinds + vector renderer + editor tool selector | source contracts; Windows render/project fixture required |
| Three Step styles | `AnnotationStepLabels`, editor layer creation, renderer | Core step-label tests + Windows render fixture |
| Cursor / click / emoji stamps | vector/text annotation layers | source contracts + Windows render fixture |
| Magnify / spotlight | on-demand canvas-local annotation rendering | source contracts + Windows pixel fixture |
| Curved line / curved arrow / bracket | vector renderer paths | source contracts + Windows render fixture |

## 3.6.0 Local Actions + Workflow Studio

| Capability | Tier | Implementation | Validation |
|---|---|---|---|
| Hash-pinned Local Action profiles | Plus / Pro | `Core/LocalActions/*`, `App/LocalActions/*`, Control Center | Core validator/template tests + static contracts; Windows process fixture required |
| Direct executable launch without shell interpolation | Plus / Pro | `LocalActionRunner`, `ProcessStartInfo.ArgumentList`, `UseShellExecute=false` | verifier contract + Windows executable fixture |
| `$input/$output/$width/$height/$ocrText/$windowTitle` templates | Plus / Pro | `LocalActionTemplate`, runner variable bridge | Core template tests + Windows workflow fixture |
| Bounded Local Action timeout/stdout/stderr/output | Plus / Pro | `LocalActionRunner`, profile validator | source gate + Windows timeout/oversize fixtures |
| Local Action output chaining | Plus / Pro | workflow executor image/text replacement path | static contract + Windows pipeline fixture |
| Visual Workflow Studio | Plus / Pro | `MainWindow.xaml(.cs)`, workflow model/store | XAML handler/static gate + Windows interaction smoke |
| Drag/drop and button step reorder | Plus / Pro | WinUI `ListView` reorder + builder item ordering | XAML/static gate + Windows drag smoke |
| Per-step enable/required/condition/retry/timeout/options | Plus / Pro | `WorkflowStep`, `WorkflowExecutor`, Studio | compatibility source test + Windows workflow smoke |
| Workflow variables + CLI overrides | Plus / Pro workflow authoring | `WorkflowVariables`, `CliParser`, executor | Core parser/validation tests + resident forwarding smoke |
| `.magicworkflow` import/export | Plus / Pro | `WorkflowStore`, Studio file pickers | bounded import validation + Windows picker smoke |
