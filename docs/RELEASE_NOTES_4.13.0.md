# Magic Capture Desktop 4.13.0 — Workflow Control Flow & Safe Resume

Magic Capture Desktop 4.13 completes audit features #424 and #432 without introducing cloud checkpoints, persisted workflow values, or a new background service.

## ForEachImage

Schema v5 adds `ForEachImage`. The step uses `Argument` as a child workflow id and runs that child over a host-supplied image set. The Workflows page can execute one workflow once with selected History captures as the image-loop set. Runtime input is capped at **32 images**; nested child loops see only their current image so accidental N×N expansion cannot occur. Child workflows receive `loop.index`, `loop.number`, and `loop.count` variables.

`continueOnError=true` continues after returned child failures and non-fatal child exceptions. A whole `ForEachImage` step cannot have `MaxAttempts > 1`, because retrying the step could repeat already-completed child-image side effects. Existing subworkflow cycle/depth limits and child tier checks remain enforced. Dry-run propagates into loop children.

## Resume failed workflow

Workflow trace metadata now optionally records the source History `AssetId`, a canonical SHA-256 `WorkflowFingerprint`, resume ancestry, and cumulative ids of already-completed side effects that are safe to suppress. It still stores no capture bytes, OCR/AI text, variable values, prompt answers, HTTP data, clipboard payloads, stdout/stderr, or Local Action output.

Resume is user-driven from the trace list. It requires a failed non-dry-run trace, the original History capture, and an unchanged workflow fingerprint. The runtime replays from the beginning to rebuild deterministic state and may request interactive inputs again. Previously completed `CopyImage`, `CopyText`, `SaveImage`, `PinImage`, and `OpenEditor` steps are skipped. Completed or failed `RunMagicAction`, `CustomHttpDestination`, `RunLocalAction`, `RunWorkflow`, or `ForEachImage` makes resume unavailable because replay may duplicate non-replayable effects or skipping may lose output/state.

Repeated resumes preserve the cumulative safe-side-effect id set in metadata, preventing a second resume from re-running an effect already suppressed by an earlier resume.

## Release truth

The exact 660-feature audit is **435 Done / 61 Partial / 106 Foundation / 36 Missing / 22 ReleaseTest = 660**. Only **#424 Loop over images** and **#432 Resume failed workflow** are promoted by this wave.

## Verification boundary

The source release runs repository, XAML structure, C# lexical, workflow-trigger, and workflow-control-flow contracts. The generation environment has no .NET/Visual Studio/Windows SDK, so xUnit execution, WinUI compilation, real History/ContentDialog behavior, x64/ARM64 builds and MSIX packaging remain mandatory Windows gates.
