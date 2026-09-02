# Magic Capture Desktop 4.12 Automation Triggers Design

## Goal

Turn the workflow-trigger foundations (#438–#444) into a bounded, local-first automation surface without adding a new Windows service, cloud daemon, or always-on polling subsystem.

## Architecture

Automation is split into five isolated units. `WorkflowTriggerPolicy` owns validation and unattended-safety rules. `WorkflowTriggerStore` and `WorkflowTriggerHistoryStore` persist bounded local configuration and metadata-only history. `WorkflowTriggerRunner` is the single execution authority: it re-resolves entitlement, workflow, capture profile, cooldown and circuit-breaker state immediately before execution. `ResidentWorkflowTriggerEngine` owns event sources that exist only while at least one matching trigger is enabled and `AdvancedWorkflows` is entitled. `WindowsTaskSchedulerService` is the only host outside the resident process and launches the packaged `magiccapture.exe --trigger <id>` execution alias for schedule triggers.

## Trigger kinds

- **Schedule** — Windows Task Scheduler, current interactive user, LIMITED privilege, local `HH:mm` + selected weekdays.
- **File change** — `FileSystemWatcher` on a fully-qualified local drive path; no UNC/network shares; bounded filter and optional recursion.
- **Clipboard change** — `AddClipboardFormatListener`; observes only the Windows change notification and never reads clipboard contents for trigger matching.
- **Foreground window** — `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` with process/title substring matching in memory only; own-process foreground events are skipped.
- **Process start** — a two-second process snapshot poll that exists only when an enabled process trigger exists.
- **Hotkey** — `RegisterHotKey`; at most sixteen workflow trigger hotkeys, using A–Z, 0–9 or F1–F24 plus at least one modifier.

## Execution and safety

At most 64 trigger definitions may exist. Every enabled trigger uses a cooldown from 1 to 3,600 seconds. The runner serializes automation, counts accepted attempts in a five-minute sliding window, suspends a trigger for ten minutes after 20 attempts, and computes cooldown from the previous attempt's completion. Resident event sources allow at most one pending event per trigger so clipboard/foreground/file storms cannot create an unbounded task queue.

Every execution re-checks `ProductFeature.AdvancedWorkflows`, the target workflow tier, and the capture profile. Trigger capture profiles must be unattended-safe: exact Region, Foreground Window, Active Monitor or Virtual Desktop. Interactive Region and Scrolling capture fail closed. When entitlement changes, the resident engine reloads and removes its watchers/hooks/hotkeys/process loop if automation is no longer entitled.

The `--trigger` CLI path accepts only safe trigger identifiers and dispatches with `expectedKind=Schedule`. A stale scheduled task therefore cannot execute a trigger whose id was later reused as File/Clipboard/Window/Process/Hotkey.

## Persistence and privacy

Trigger configuration is atomic local JSON with the existing local-configuration size limits. Trigger history retains at most 200 entries and stores only trigger id/name/kind, status, reason code, start time and completion time. It never stores changed file names, clipboard text, foreground-window titles, process command lines, capture pixels, OCR/AI values, HTTP payloads or Local Action output. History persistence is best-effort and never changes whether an automation action succeeded.

## UI

The Trigger Manager provides list + editor + history in one WinUI window. Users can create, enable/disable, edit, save, test and delete triggers; choose an existing capture profile/workflow; configure kind-specific fields; see hotkey registration failures; and clear/refresh metadata history. Schedule registration failure disables the saved trigger rather than leaving a configuration that claims to be active.

## Lifecycle

No trigger source exists at app startup unless configuration and entitlement require it. File/clipboard/window/hotkey sources are event-driven. Process polling runs only with process-start triggers. Closing the Control Center leaves configured resident triggers active in the tray host; explicit Exit tears down all trigger sources. CLI/scheduled activation remains headless and does not show the trial-expired dialog.

## Non-goals

4.12 does not implement in-workflow image loops (#424), arbitrary workflow checkpoint/resume (#432), a Windows service, remote/cloud scheduling, cron syntax, network-share watchers, clipboard-content matching, process command-line matching, or unattended interactive scrolling capture.

## Release truth

When the source, UI, persistence and release gates pass, #438–#444 move to Done. The exact 660-feature totals become **433 Done / 61 Partial / 108 Foundation / 36 Missing / 22 ReleaseTest**. `Done` remains source-truth only; real WinUI compilation, Task Scheduler registration, P/Invoke behavior, x64/ARM64 builds and MSIX packaging are Windows release gates.
