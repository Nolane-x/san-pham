# Magic Capture Desktop 4.13 Workflow Control Flow & Safe Resume Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded image-loop control flow and privacy-preserving safe resume for failed workflows, then ship reproducible source version 4.13.0.

**Architecture:** Schema v5 adds `ForEachImage`, executed through existing child-workflow resolution with bounded loop assets and the existing call-stack guard. Resume uses metadata-only trace fields plus a canonical workflow SHA-256 fingerprint; execution replays pure steps and skips only a narrow allowlist of already-completed side effects.

**Tech Stack:** .NET 10, WinUI 3, C#, System.Text.Json, SHA-256, existing AtomicJsonFile/history/workflow infrastructure, Python source verifiers.

**Spec:** `docs/superpowers/specs/2026-08-26-magic-capture-desktop-4.13-workflow-control-flow-resume-design.md`

## Global Constraints

- Local-first: no cloud checkpoint service and no new resident/background daemon.
- Persist no capture pixels, OCR/AI text, variables, prompt answers, HTTP payloads, stdout/stderr, or action outputs in workflow traces/checkpoints.
- Schemas 1–4 remain readable; only schema 5 may contain v5-only steps.
- Loop images <= 32; workflow batch remains <= 500.
- Resume is fail-closed for changed workflows or non-replayable completed side effects.
- Existing repository, structure, lexical, workflow-trigger, and source-release gates remain mandatory.

---

### Task 1: Schema v5, loop policy and fingerprint primitives

**Files:**
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowModels.cs`
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowRuntimePolicy.cs`
- Modify: `src/Magic.Capture.Core/Workflows/WorkflowValidator.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowFingerprint.cs`
- Create: `scripts/verify-workflow-control-flow.py`

**Interfaces:**
- Produces `WorkflowStepKind.ForEachImage`, `MaximumLoopImages`, `ParseLoopContinueOnError`, v5 schema gating, resume-side-effect classification, and `WorkflowFingerprint.Compute(CaptureWorkflow)`.

- [x] Add a failing source contract for schema 5, loop bounds/options, resume classification, and deterministic 64-hex fingerprint.
- [x] Run the contract and confirm it fails only because v5 primitives are absent.
- [x] Add `ForEachImage`, schema-v5 policy/validation, bounded loop option parsing, safe/non-replayable resume classifications, and canonical SHA-256 fingerprinting.
- [x] Re-run control-flow, repository, structure, lexical, and trigger contracts.

### Task 2: Bounded ForEachImage runtime

**Files:**
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutionContext.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutor.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Consumes child resolver/call stack.
- Produces `LoopAssets`, `WorkflowLoopSummary`, and `ForEachImage` execution with `loop.index`, `loop.number`, `loop.count`.

- [x] Extend the failing contract for loop context, child execution, one-item fallback, max-32 enforcement, continue-on-error, dry-run propagation, and nested-loop collapse.
- [x] Run the contract and confirm the new runtime checks fail.
- [x] Implement minimal bounded loop execution and loop-variable injection without aggregating payloads.
- [x] Re-run all static/source contracts.

### Task 3: Trace metadata and safe resume planner

**Files:**
- Modify: `src/Magic.Capture.App/Workflows/WorkflowTraceStore.cs`
- Create: `src/Magic.Capture.App/Workflows/WorkflowResumePlanner.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Produces optional trace `AssetId`, `WorkflowFingerprint`, `ResumedFromTraceId` and `WorkflowResumePlan` with completed safe side-effect ids.

- [x] Extend the contract for metadata-only trace identity/fingerprint/ancestry and fail-closed resume planning.
- [x] Run RED and confirm trace/resume checks fail.
- [x] Add backward-compatible trace fields/validation and update single, automation, dry-run, and batch trace call sites to include asset ids.
- [x] Implement resume planner: reject success/dry-run/stale fingerprint/missing asset/non-replayable completed prefix/loop prefix; allow preflight failure and safe-side-effect prefixes.
- [x] Re-run all static/source contracts.

### Task 4: Resume executor semantics

**Files:**
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutionContext.cs`
- Modify: `src/Magic.Capture.App/Workflows/WorkflowExecutor.cs`

**Interfaces:**
- Produces `IsResume` plus `ResumeCompletedSideEffectStepIds`; replay reconstructs pure state while safe completed side effects are skipped.

- [x] Add RED checks for resume context and executor skip placement after condition evaluation/before action execution.
- [x] Implement fail-closed skip semantics restricted to `WorkflowRuntimePolicy.IsResumeSkippableSideEffect`.
- [x] Preserve cancellation/retry/condition/dry-run behavior and emit an in-memory skipped result for already-completed side effects.
- [x] Re-run all static/source contracts.

### Task 5: Workflow Studio loop and resume UX

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces History-selected loop execution and trace resume UX using existing HistoryStore/PrepareWorkflowAsset privacy path.

- [x] Add RED XAML/source checks for `RunWorkflowLoopOnHistory_Click`, `ResumeWorkflowTrace_Click`, v5 hints, and resume trace details.
- [x] Implement one-run selected-History loop loading with resident-selection byte limits and primary-first execution.
- [x] Implement resume: resolve workflow, plan, locate source History asset by trace AssetId, apply redaction, replay with skip set, append ancestry trace, refresh UI.
- [x] Explain non-resumable reasons without deleting/changing traces.
- [x] Re-run structure/lexical/repository/control-flow/trigger gates.

### Task 6: Tests and hardening

**Files:**
- Create or modify: `tests/Magic.Capture.Core.Tests/WorkflowControlFlowTests.cs`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`

**Interfaces:**
- Produces Windows runtime tests for loop bounds/failure modes/fingerprint stability/resume eligibility/side-effect skipping/history asset missing/cancellation.

- [x] Add xUnit tests for deterministic fingerprint, v5 validation, loop option parsing, and resume classifications that can compile on Windows.
- [x] Add Windows checklist scenarios for actual WinUI loop selection and resume, including changed workflow, deleted History asset, and side-effect prefix rejection.
- [x] Run available static gates here and record Windows-only runtime gates honestly.

### Task 7: Release truth and reproducible 4.13.0 package

**Files:**
- Modify: `release/version.json`
- Modify: `release/feature-audit-660.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `README.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_4.13.0.md`
- Modify: `scripts/source-release.py`

**Interfaces:**
- Produces `4.13.0` / `4.13.0.0`; promotes only #424 and #432 when code/UI/trace gates are end-to-end.

- [x] Add release-contract assertions before changing metadata.
- [x] Promote only #424 and #432 and assert every other audit row is byte-for-byte unchanged apart from rendered summary surfaces.
- [x] Synchronize four version sources, README, matrix, release notes, checklist, and source-release verifier chain.
- [x] Run repository, structure, lexical, trigger, and control-flow gates plus audit/version sanity.
- [x] Produce provisional deterministic A/B ZIPs and verify byte-for-byte identity.
- [x] Extract provisional ZIP, rerun every gate, then mark packaging tasks complete.
- [x] Produce final A2/B2 ZIPs from the completed plan and verify byte-for-byte identity, ZIP integrity, checksum sidecar, version/audit, and all static contracts from the exact delivery ZIP.
