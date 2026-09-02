# Magic Capture 4.11 Workflow Runtime v4 Design

## Goal

Turn the existing Workflow Studio into a bounded, interactive, reusable local workflow runtime without adding background automation or cloud dependencies.

## Scope

This wave implements feature-audit items 420, 421, 422, 423, 425, 426, 427, 430, 431, and 433 when their end-to-end gates are satisfied. Item 424 remains Foundation unless an actual in-workflow image-collection loop is implemented. Item 432 remains Foundation because safe resume across arbitrary side effects requires a dedicated checkpoint design. Items 438–444 are intentionally deferred to a later automation-trigger wave.

## Architecture

### Workflow schema v4

`CaptureWorkflow` gains bounded typed parameter definitions. Parameters are distinct from existing `Variables`: variables remain defaults/constants; parameters describe runtime values that a host may resolve from CLI input, supplied variables, or an interactive prompt. The validator owns all names, lengths, choice counts, default compatibility, and schema bounds.

New step kinds are:

- `PromptText` — asks for a bounded string and writes it to `OutputKey`.
- `PromptChoice` — asks the user to choose from a bounded list and writes it to `OutputKey`.
- `Confirm` — asks for explicit confirmation and writes `true`/`false`; when Required and declined, the step fails cleanly.
- `Delay` — waits a bounded number of milliseconds with cancellation.
- `RunWorkflow` — invokes another workflow on the current image/context using the same runtime values and interaction callbacks.

Subworkflow execution has a maximum depth of 4 and rejects cycles using a case-sensitive workflow-id call stack. A child receives a copy of the parent runtime string values plus the current image. Child output is namespaced under the step output key when possible, while current `image` and `text` flow back to the parent.

### Parameter resolution

Resolution order is runtime/CLI values → workflow variable defaults → parameter default → interactive host callback. Required values with no source fail before the first workflow step. Choice values must match one declared choice exactly. Boolean parameters accept only `true`/`false`.

The Workflows page gets a parameter editor with explicit fields instead of a free-form JSON box. Interactive execution uses WinUI `ContentDialog`; non-interactive CLI execution fails with a clear message when a required parameter is missing rather than silently inventing input.

### Batch runtime

The existing History multi-selection loop moves into a dedicated `WorkflowBatchRunner`. It accepts at most 500 assets, executes sequentially to preserve interactive-dialog and clipboard semantics, resolves top-level workflow parameters once, supports cancellation, records per-item success/failure, and never retains image bytes after an item completes. This is the bounded implementation of batch workflow (#425), not an unbounded parallel queue.

### Dry-run

`WorkflowExecutionContext.DryRun` enables real validation and execution of deterministic local analysis/transformation steps while suppressing side-effecting or externally interactive actions. Suppressed steps produce trace entries marked `WouldRun`. Dry-run never writes files, clipboard, pins, opens windows, sends HTTP, starts local actions, invokes AI providers, prompts the user, or sleeps. Conditions and already-available values are still evaluated.

A Studio button runs dry-run on one selected History capture and displays the step plan/result.

### Trace and step logs

Every execution produces a structured trace with:

- trace id, workflow id/name/schema, start/end UTC, success/dry-run,
- per-step id/kind, status, attempts, start/end UTC, duration, bounded error category/message,
- no image bytes, OCR text, AI response, clipboard data, variables, HTTP bodies, local-action stdout/stderr, or secrets.

`WorkflowTraceStore` persists the newest 100 traces under LocalAppData using atomic JSON writes and a strict total-file/entry size policy. The Workflows page exposes recent trace summaries and selected trace details. This upgrades execution trace and step logs without turning logs into a sensitive-data archive.

## Safety and limits

- No background worker or trigger is introduced.
- Workflow step cap remains 64.
- Parameter cap: 24 per workflow.
- Parameter/variable names: 1–64 characters using the existing workflow-variable naming policy.
- Parameter prompt: maximum 240 characters.
- Choice parameters: 2–24 choices, each maximum 160 characters.
- Text parameter values: maximum 4096 characters.
- Delay: 0–60,000 ms.
- Subworkflow depth: maximum 4; cycles rejected.
- Batch assets: maximum 500 and sequential execution.
- Trace store: maximum 100 traces; no payload values or image bytes.
- Imported workflows are validated before entering the editor or store.
- Existing schemas 1–3 remain readable; new saves use schema 4.

## Error behavior

Validation errors are deterministic and block save/import/execution. User cancellation of a prompt is reported as a normal workflow-step failure rather than an app crash. Subworkflow lookup/cycle/depth errors fail the calling step. Dry-run reports suppressed actions as successful previews rather than performing them. Trace persistence failure is logged but must not change workflow execution success.

## Testing

Core tests cover parameter validation/resolution, delay bounds, subworkflow recursion policy, step side-effect classification, and schema backward compatibility. Source-contract tests require UI callbacks, parameter editor, dry-run button, trace store, batch runner, release truth, and feature statuses. Existing repository/structure/C# lexical gates must remain green. Windows release checklist gains xUnit, WinUI compile, dialog, dry-run side-effect, subworkflow-cycle, batch, and trace privacy tests.
