# Magic Capture Desktop 4.11.0 — Workflow Runtime v4

Magic Capture Desktop 4.11.0 turns the existing Workflow Studio foundation into a bounded interactive/reusable local runtime. This wave adds no background scheduler, file watcher, resident workflow worker, or new cloud dependency.

## Typed parameters and interactive steps

Schema 4 adds up to 24 **typed parameters** (`Text`, `Choice`, `Boolean`). Runtime resolution is deterministic: supplied runtime/CLI values, then workflow variables, then parameter defaults, then an explicit host prompt. Required unresolved parameters fail before step execution; dry-run never prompts. Existing workflow schemas 1–3 remain readable, while v4-only parameters or step kinds mislabeled as an older schema fail validation.

The v4 step set adds **Prompt Text**, **Prompt Choice**, confirmation, bounded Delay, and reusable `RunWorkflow` subworkflows. Prompt values and choices are bounded, Delay is capped at 60,000 ms, and subworkflows reject cycles and nesting beyond four workflow levels. Child workflows keep their own entitlement checks.

## Batch, dry-run and privacy-safe traces

History multi-selection now uses `WorkflowBatchRunner`: at most **500** captures, sequential execution, one shared top-level parameter resolution, cancellation support, and lazy per-item image loading so the runner does not retain a capture collection in memory. Existing outbound redaction remains in front of real batch execution; subworkflow graph scanning tracks the shallowest seen path so alternate graph routes cannot hide a reachable Copy/Save/Pin action from the privacy policy.

Studio dry-run validates and executes deterministic local analysis but suppresses external or interactive actions as `WouldRun`. It never writes clipboard/files/pins, opens editor windows, performs HTTP/AI/Local Action calls, prompts the user, or sleeps.

`WorkflowTraceStore` retains the newest **100** local traces with workflow/step ids, kinds, status, attempts and timing metadata. Traces are intentionally **privacy-safe**: they never serialize capture pixels, OCR/AI text, variable values, HTTP bodies, clipboard payloads, Local Action stdout/stderr, or executor result values. Failures that occur during parameter/preflight before a normal result exists also write a payload-free failed trace best-effort.

## Release truth

The exact 660-feature audit is now **426 Done / 62 Partial / 114 Foundation / 36 Missing / 22 ReleaseTest = 660**. Features **#420, #421, #422, #423, #425, #426, #427, #430, #431 and #433** move to Done because Core → runtime → UI/persistence wiring exists in source.

Feature **#424 Loop over images** remains Foundation: 4.11 batch execution is not an in-workflow image-collection loop. **#432 Resume failed workflow** remains Foundation because safe checkpoint/resume across arbitrary side effects needs a dedicated design. Task Scheduler and trigger automation **#438–#443** remain Foundation, and **#444 Hotkey trigger** remains Partial.

## Verification boundary

Repository/source-contract, XAML-structure and C# lexical gates are required before the source ZIP is produced. The current Linux generation environment does not provide .NET/Visual Studio/Windows SDK, so xUnit execution, WinUI compilation, x64/ARM64 builds, MSIX packaging, real ContentDialog behavior, cancellation timing and external-side-effect assertions remain mandatory Windows gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
