# Magic Capture Desktop 4.12.0 — Automation Triggers

Magic Capture Desktop 4.12 completes the local workflow-trigger surface represented by audit features #438–#444. It adds no backend, cloud scheduler or Windows service. Schedule triggers use Windows Task Scheduler; all other trigger sources live inside the existing tray-resident process and are created only when enabled and entitled.

## Six local trigger kinds

The Trigger Manager can create, edit, enable/disable, test and delete **Schedule**, **File Change**, **Clipboard Change**, **Foreground Window**, **Process Start** and **Hotkey** triggers. Each trigger targets an existing capture profile and workflow.

Windows Task Scheduler registration runs `schtasks.exe` directly with `ProcessStartInfo.ArgumentList`, never through `cmd.exe` or PowerShell. Tasks run for the current interactive user at LIMITED privilege and launch the packaged `magiccapture.exe --trigger <id>` alias. Trigger ids use one safe identifier policy across Core, persistence, CLI and scheduler. The CLI execution path accepts Schedule as the expected kind, so a stale scheduled task cannot execute an id that was later reused as File/Clipboard/Window/Process/Hotkey.

File triggers use a bounded local-drive `FileSystemWatcher`; UNC/network paths are rejected. Clipboard triggers observe only the Windows clipboard-change message and never read clipboard contents for matching. Foreground-window triggers compare process/title text in memory only and skip Magic Capture Desktop's own foreground events. Process-start polling runs every two seconds only when an enabled process trigger exists. Workflow trigger hotkeys use `RegisterHotKey`, are capped at 16 and accept A–Z, 0–9 or F1–F24 with at least one modifier.

## Bounded execution and entitlement safety

Configuration is capped at **64 triggers**. Cooldown is 1–3,600 seconds and starts when an accepted attempt completes. Each resident trigger can have only one pending event, preventing clipboard/window/file storms from building an unbounded task queue. A central runner serializes automation and applies a circuit breaker after **20 accepted attempts in five minutes**, suspending the trigger for ten minutes.

Every run re-resolves the current workflow, workflow tier and capture profile and requires `AdvancedWorkflows`. Only unattended-safe exact Region, Foreground Window, Active Monitor and Virtual Desktop profiles may run; interactive Region and Scrolling capture fail closed. Entitlement changes reload the resident engine, removing file watchers, clipboard listener, foreground hook, process timer and workflow hotkeys when automation is no longer entitled.

Manual capture-profile behavior remains unchanged: interactive Region and Scrolling profiles still work from the user-driven path, while the automation path propagates failures so trigger history cannot report success after a failed capture/workflow.

## Privacy-safe local history

Trigger history is **metadata-only** atomic local storage capped at **200** entries. It persists only trigger id/name/kind, status, reason code and start/completion timing. It never stores changed file names, clipboard contents, foreground-window titles, process command lines, capture pixels, OCR/AI text, variables, HTTP bodies, clipboard payloads or Local Action output. History persistence is best-effort and cannot turn a successfully completed automation into an execution failure.

## Release truth

The exact 660-feature audit is **433 Done / 61 Partial / 108 Foundation / 36 Missing / 22 ReleaseTest = 660**. Features **#438 Windows Task Scheduler integration, #439 Schedule workflow locally, #440 File watcher trigger, #441 Clipboard trigger, #442 Target-window-change trigger, #443 Process-start trigger and #444 Hotkey trigger** move to Done because Core → persistence → runner → resident/Task Scheduler source → Trigger Manager wiring exists end-to-end in source.

Loop-over-images (#424) and arbitrary resume/checkpoint semantics (#432) remain Foundation and are not claimed by this wave.

## Verification boundary

The source release must pass repository, XAML-structure, C# lexical and workflow-trigger source-contract gates and must be reproducible byte-for-byte when packaged twice. The Linux generation environment does not contain .NET/Visual Studio/Windows SDK, so xUnit execution, WinUI compilation, real Task Scheduler creation/deletion, P/Invoke behavior, hotkey collision behavior, x64/ARM64 builds and MSIX packaging remain mandatory Windows gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
