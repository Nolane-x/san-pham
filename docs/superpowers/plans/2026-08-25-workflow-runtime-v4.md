# Workflow Runtime v4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver Magic Capture 4.11 as a bounded interactive/reusable workflow runtime with typed parameters, prompt/choice/confirm/delay/subworkflow steps, batch execution, dry-run, and privacy-safe local traces.

**Architecture:** Extend the existing Core workflow schema and validator first, then layer App callbacks/runtime, batch execution, trace persistence, and WinUI controls on those contracts. All new runtime paths reuse the existing `WorkflowExecutor`, `WorkflowStore`, entitlement checks, and configuration safety limits; no background trigger service is added.

**Tech Stack:** .NET 10, C#, WinUI 3, System.Text.Json, existing Magic Capture atomic persistence and static verifier scripts.

**Spec:** `docs/superpowers/specs/2026-08-25-workflow-runtime-v4-design.md`

## Global Constraints

- Local-first; no new cloud dependency or background worker.
- Existing workflow schemas 1–3 remain readable; new saves use schema 4.
- Maximum 64 workflow steps, 24 parameters, 500 batch assets, 4 nested workflows, 100 local traces.
- Dry-run performs no side effects or user prompts.
- Trace persistence must not include images, OCR/AI text, variable values, HTTP bodies, clipboard payloads, or local-action stdout/stderr.
- The Linux source bundle cannot prove WinUI/xUnit/MSIX runtime gates; those remain explicit Windows release gates.

---

### Task 1: Core workflow v4 contracts and validator

**Files:**
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowModels.cs`
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowValidator.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowParameterResolver.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowRuntimePolicy.cs`
- Test: `tests/Magic.Capture.Core.Tests/WorkflowV4Tests.cs`

**Interfaces:**
- Produces `WorkflowParameterKind`, `WorkflowParameterDefinition`, `CaptureWorkflow.Parameters`, new `WorkflowStepKind` members, parameter validation/resolution, and side-effect/runtime policy helpers.

- [x] Add failing source-contract/core tests for schema 4, parameter bounds, choice/default compatibility, delay parsing, and side-effect classification.
- [x] Run the available static/source contract and confirm failures are caused by missing v4 contracts.
- [x] Implement the minimum Core contracts and validator rules.
- [x] Re-run the contract and existing static gates.

### Task 2: Interactive executor and subworkflow safety

**Files:**
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutionContext.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutor.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowStore.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Consumes v4 Core contracts.
- Produces host callbacks for text/choice/confirm, dry-run execution, and bounded `RunWorkflow` recursion.

- [x] Add failing contract assertions for callbacks, dry-run suppression, cycle/depth checks, and parameter preflight.
- [x] Implement parameter resolution before step execution.
- [x] Implement PromptText, PromptChoice, Confirm, Delay, and RunWorkflow.
- [x] Implement dry-run side-effect suppression and deterministic preview statuses.
- [x] Re-run static gates and inspect call sites/signatures for compile risks.

### Task 3: Batch runtime and trace persistence

**Files:**
- Create: `src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs`
- Create: `src/Magic.Capture.App/Workflows/WorkflowTraceStore.cs`
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Modify: `src/Magic.Capture.App/Persistence/LocalConfigurationLimits.cs` (or the existing equivalent limits file)
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Produces sequential bounded batch summaries and privacy-safe trace records/store.

- [x] Add failing contract assertions for 500-item cap, trace cap, atomic JSON persistence, and forbidden-payload absence.
- [x] Implement batch runner with cancellation and per-item summary.
- [x] Implement trace models/store with bounded messages and newest-100 retention.
- [x] Persist traces after workflow execution without changing execution outcome on trace-write failure.
- [x] Re-run static gates.

### Task 4: Workflow Studio v4 UX

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes workflow parameters, batch runner, dry-run, and trace store.
- Produces parameter editor, dry-run button, batch summary, recent trace list/detail, and interaction dialogs.

- [x] Add failing structure/source-contract assertions for parameter and trace controls/handlers.
- [x] Add parameter list/editor controls with Add/Apply/Remove and bounded choice editing.
- [x] Wire load/new/duplicate/save/import/export so v4 parameters round-trip.
- [x] Replace manual History batch loop with `WorkflowBatchRunner` and resolve parameters once.
- [x] Add dry-run on one selected History capture.
- [x] Add recent trace refresh/details UI.
- [x] Re-run XAML handler/lexical/repository gates.

### Task 5: Release truth and source release

**Files:**
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `release/feature-audit-660.json`
- Verify unchanged identity source: `docs/feature-audit/feature-backlog-660.json`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `README.md`
- Create: `docs/RELEASE_NOTES_4.11.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Modify: this plan checklist after each verified task.

**Interfaces:**
- Produces truthful 4.11.0 source release metadata and reproducible ZIP.

- [x] Promote only features proven end-to-end by this wave; keep loop/resume/triggers at their prior status unless separately completed.
- [x] Set source version 4.11.0 and MSIX source version 4.11.0.0.
- [x] Run fresh repository, structure, lexical, audit-count, and version gates.
- [x] Build the source ZIP twice using `scripts/source-release.py`; require byte-identical output and identical SHA-256.
- [x] Extract the final delivery ZIP to a new directory and rerun every static/audit/version gate from that extracted copy.
- [x] Write the final `.sha256` sidecar and mark all plan steps complete only after evidence exists.
