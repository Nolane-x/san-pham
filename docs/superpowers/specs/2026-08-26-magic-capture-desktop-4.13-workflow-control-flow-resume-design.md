# Magic Capture Desktop 4.13 — Workflow Control Flow & Safe Resume Design

## Goal

Complete feature #424 (Loop over images) and #432 (Resume failed workflow) without weakening local-first privacy, deterministic workflow validation, or side-effect safety.

## Scope

4.13 adds one schema-v5 workflow step, `ForEachImage`, plus trace-backed safe resume. It does not add arbitrary loops, persisted runtime values, cloud checkpoints, background workers, or automatic retry after restart.

## ForEachImage semantics

`ForEachImage` takes a child workflow id in `Argument`. The host supplies a bounded `LoopAssets` list; the History UI can run one workflow once with selected captures as this list. If a host supplies no list, the current asset becomes a one-item loop.

The runtime cap is 32 images. Each child receives `loop.index`, `loop.number`, and `loop.count` string variables. Nested child execution reuses the existing workflow call-stack cycle/depth guard and receives only its current image as its own loop set, preventing accidental N×N expansion. The option `continueOnError=true|false` controls whether one child failure aborts the parent loop. An optional output key stores a metadata-only in-memory `WorkflowLoopSummary` with requested/succeeded/failed counts; image/result payloads are not aggregated.

Dry-run propagates to children. `ForEachImage` requires workflow schema 5. Schemas 1–4 remain readable and cannot claim v5-only steps.

## Resume semantics

Workflow traces become resumability metadata by adding optional `AssetId`, `WorkflowFingerprint`, and `ResumedFromTraceId`. The fingerprint is SHA-256 over a canonical representation of workflow structure/configuration; the trace stores only the digest, not runtime values.

Resume always loads the original capture from local History, verifies the workflow id/fingerprint still match, and replays the workflow from the beginning to reconstruct pure state. Previously succeeded side effects are skipped only for the explicitly safe set: `CopyImage`, `CopyText`, `SaveImage`, `PinImage`, and `OpenEditor`.

Resume is rejected if the completed prefix contains a succeeded non-replayable operation (`RunMagicAction`, `CustomHttpDestination`, `RunLocalAction`, `RunWorkflow`, or `ForEachImage`) because replay could duplicate external effects while skipping could lose output/state. Interactive prompts and deterministic transformations are replayed and may ask the user again.

A failed dry-run, successful trace, missing History asset, stale workflow fingerprint, invalid trace, or changed workflow is not resumable. No checkpoint stores OCR text, AI output, image bytes, variables, HTTP data, stdout/stderr, prompt answers, or local-action output.

## UI

Workflow Studio receives a `Run once with selected History as image loop` action and v5 editor guidance for `ForEachImage` (`Argument=child workflow id`, option `continueOnError=true|false`). Trace controls gain `Resume selected failed trace`. Trace details show source capture id, workflow fingerprint availability, and resume ancestry without exposing payload.

## Error handling and safety

Loop counts, trace counts, schema versions, fingerprints, ids, and timestamps are validated on load. Resume planning is deterministic and fail-closed. Trace persistence remains best-effort so trace I/O cannot change workflow success. Cancellation propagates normally.

## Verification

A new source contract verifies schema-v5 gating, loop bounds, child-call-stack behavior, trace privacy fields, fingerprinting, safe resume policy, executor skip semantics, UI handlers, release metadata, and source-release integration. Existing repository/structure/lexical/trigger contracts remain mandatory. Windows xUnit/WinUI/MSIX runtime gates remain required on Windows because this environment lacks the Windows/.NET SDK.
