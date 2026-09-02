# Magic Capture Desktop 4.12 Automation Triggers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete features #438–#444 as a bounded local workflow-trigger runtime with Windows Task Scheduler and entitlement-aware resident sources.

**Architecture:** Trigger validation stays in Core; App owns atomic stores, the central serialized runner, event-source lifecycle, Windows Task Scheduler integration and WinUI management. Every source converges on `WorkflowTriggerRunner.RunAsync`, which re-checks entitlement/workflow/profile immediately before execution and writes privacy-safe metadata history best-effort.

**Tech Stack:** .NET 10, WinUI 3, Win32 `RegisterHotKey`, clipboard listener, WinEvent hooks, `FileSystemWatcher`, `PeriodicTimer`, Windows `schtasks.exe`, JSON/atomic local persistence.

**Spec:** `docs/superpowers/specs/2026-08-26-magic-capture-desktop-4.12-automation-triggers-design.md`

## Global Constraints

- No backend, cloud scheduler or Windows service.
- Resident event sources exist only when enabled and `AdvancedWorkflows` is entitled.
- Maximum 64 triggers; maximum 16 workflow hotkeys.
- Cooldown 1–3,600 seconds; hard circuit breaker 20 attempts / 5 minutes, suspended for 10 minutes.
- Trigger history is metadata-only and capped at 200 entries.
- Unattended capture rejects interactive Region and Scrolling profiles.
- Scheduled CLI dispatch accepts Schedule triggers only.
- Windows runtime/build/MSIX claims require a real Windows gate; Linux static verification is not a substitute.

---

### Task 1: Core trigger schema and policy

**Files:**
- Create: `src/Magic.Capture.Core/Workflows/WorkflowTriggerModels.cs`
- Create: `src/Magic.Capture.Core/Workflows/WorkflowTriggerPolicy.cs`
- Modify: `src/Magic.Capture.Core/Cli/CliCommand.cs`
- Modify: `src/Magic.Capture.Core/Cli/CliParser.cs`
- Test: `tests/Magic.Capture.Core.Tests/WorkflowTriggerTests.cs`
- Test: `tests/Magic.Capture.Core.Tests/CliParserTests.cs`

**Interfaces:**
- Produces `WorkflowTrigger`, `WorkflowTriggerKind`, kind-specific option records, `WorkflowTriggerPolicy.Validate/ValidateSet`, `TriggerCliCommand`.

- [x] Add source-contract assertions and verify they fail before production symbols exist.
- [x] Implement bounded models, safe ids, Windows-local file paths, schedule/day rules, supported hotkeys and unattended-profile policy.
- [x] Add `--trigger <id>` parsing using the same safe-id policy.
- [x] Add Core/xUnit source cases for duplicate hotkeys, exact-region safety, cross-platform Windows path validation and trigger CLI ids.
- [x] Re-run the trigger contract and static gates.

### Task 2: Atomic configuration and metadata history

**Files:**
- Create: `src/Magic.Capture.App/Workflows/WorkflowTriggerStore.cs`
- Create: `src/Magic.Capture.App/Workflows/WorkflowTriggerHistoryStore.cs`
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Modify: `src/Magic.Capture.App/Persistence/LocalConfigurationLimits.cs`

**Interfaces:**
- Produces atomic `LoadAsync/SaveAsync/DeleteAsync` trigger configuration and a newest-200 metadata history.

- [x] Write failing source-contract checks for trigger/history storage.
- [x] Implement atomic bounded JSON stores and validate the full trigger set on read/write.
- [x] Ensure history persists only ids/names/kind/status/reason/timing and treats persistence as telemetry, not execution authority.
- [x] Re-run trigger/static gates.

### Task 3: Central automation runner

**Files:**
- Create: `src/Magic.Capture.App/Workflows/WorkflowTriggerRunner.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Consumes trigger store/history, workflow store, entitlement service and current capture profiles.
- Produces serialized `RunAsync(triggerId, expectedKind, reasonCode, cancellationToken)`.

- [x] Write contract assertions for entitlement, kind-gating, serialized execution, cooldown and circuit breaker.
- [x] Implement fresh workflow/profile/tier resolution and `AdvancedWorkflows` entitlement enforcement.
- [x] Split manual capture-profile execution from the automation path so automation propagates failures while manual interactive Region/Scrolling behavior remains intact.
- [x] Count accepted attempts, compute cooldown from completion, and make history best-effort.
- [x] Re-run trigger/static gates.

### Task 4: Entitlement-aware resident trigger sources

**Files:**
- Create: `src/Magic.Capture.App/Workflows/WorkflowTriggerHotkeyService.cs`
- Create: `src/Magic.Capture.App/Workflows/ResidentWorkflowTriggerEngine.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeMethods.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeConstants.cs`

**Interfaces:**
- Produces `ReloadAsync/StopAsync`, bounded file/clipboard/window/process/hotkey sources and hotkey-registration diagnostics.

- [x] Write source-contract checks for each event source and entitlement teardown.
- [x] Implement event-driven file/clipboard/foreground/hotkey sources and process-only two-second polling.
- [x] Skip own-process foreground events and coalesce file changes.
- [x] Bound resident event storms to one pending event per trigger.
- [x] Tear down all sources on entitlement loss/reload/exit.
- [x] Re-run trigger/static gates.

### Task 5: Windows Task Scheduler, DI and CLI activation

**Files:**
- Create: `src/Magic.Capture.App/Workflows/WindowsTaskSchedulerService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:**
- Produces schedule `CreateOrUpdateAsync/DeleteAsync` and `--trigger` dispatch with `expectedKind=Schedule`.

- [x] Write failing contract checks for `schtasks.exe`, `ArgumentList`, LIMITED/interactive-user semantics and CLI kind-gate.
- [x] Register schedule tasks using a flat deterministic task name and the packaged `magiccapture.exe` alias without `cmd.exe`/PowerShell.
- [x] Keep scheduled/CLI activation headless, including trial-expired cases.
- [x] Wire DI, initialization, settings reload, entitlement reload and exit teardown.
- [x] Re-run trigger/static gates.

### Task 6: Trigger Manager UI

**Files:**
- Create: `src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml`
- Create: `src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces create/edit/save/delete/test/history UX for all six trigger kinds.

- [x] Write XAML/source-contract checks for Trigger Manager entry point and handlers.
- [x] Implement type-specific fields, workflow/profile selection, enable/cooldown, schedule registration, hotkey diagnostics and metadata history.
- [x] Persist enabled schedules as Disabled first, register the Windows task, then commit Enabled; delete local authority before best-effort task cleanup.
- [x] Make stale schedule deletion harmless through runtime kind-gating.
- [x] Re-run structure/lexical/trigger/repository gates.

### Task 7: Release truth and reproducible source package

**Files:**
- Modify: `release/version.json`
- Modify: `release/feature-audit-660.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `README.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_4.12.0.md`
- Modify: `scripts/source-release.py`
- Modify: `scripts/verify-repo.py`
- Modify: `scripts/verify-workflow-triggers.py`

**Interfaces:**
- Produces source release `4.12.0` / MSIX source `4.12.0.0` and exact audit totals 433/61/108/36/22.

- [x] Add release-contract checks first and verify they fail on 4.11 metadata.
- [x] Promote only #438–#444 to Done and assert all other feature rows remain unchanged.
- [x] Synchronize versions, README, matrix, release notes and Windows runtime checklist.
- [x] Make `source-release.py` execute the trigger source contract.
- [x] Run repository, structure, lexical and trigger gates on the source tree.
- [x] Produce two independent deterministic ZIPs and compare byte-for-byte/SHA-256.
- [x] Extract the final delivery ZIP into a new directory and rerun all gates, version/audit checks, ZIP integrity and checksum verification.
