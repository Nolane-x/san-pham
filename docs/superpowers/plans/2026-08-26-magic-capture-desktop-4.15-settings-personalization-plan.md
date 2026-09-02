# Magic Capture Desktop 4.15 Settings & Personalization Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish runtime personalization for hotkeys, editor/tool layouts, saved styles, monitor preferences and per-app capture rules.

**Architecture:** Extend bounded `AppSettings` schema v2 and normalize all values in Core. App services consume only normalized records; hotkey registration is rollback-safe and editor/context behavior resolves settings at dispatch time.

**Tech Stack:** .NET 10, C#, WinUI 3, Win32 `RegisterHotKey`, existing atomic settings JSON.

**Spec:** `docs/superpowers/specs/2026-08-26-magic-capture-desktop-4.15-settings-personalization-design.md`

## Global Constraints

- No cloud/backend/service dependency.
- Preserve settings recovery-mode write protection.
- Dynamic hotkeys: maximum 48; A-Z/0-9/F1-F24 plus modifier.
- Saved styles <= 24, monitor preferences <= 32, app rules <= 64.
- Do not persist image/text/AI/clipboard payloads.
- Promote only feature IDs 588-591 and 596-605.

---

### Task 1: Core personalization schema and policy

**Files:**
- Modify: `src/Magic.Capture.Core/Settings/AppSettings.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettingsRules.cs`
- Create: `src/Magic.Capture.Core/Settings/PersonalizationModels.cs`
- Create: `tests/Magic.Capture.Core.Tests/PersonalizationSettingsTests.cs`
- Create: `scripts/verify-settings-personalization.py`

**Produces:** normalized `PersonalHotkeys`, `ToolbarActions`, `OverlayActions`, annotation preferences/styles, `MonitorPreferences`, `AppCaptureRules`, plus section-reset helpers.

- [x] Add source-contract/test cases for all v2 fields, bounds, duplicate gestures, safe target IDs, style/action allowlists and context-rule normalization; run `python scripts/verify-settings-personalization.py` and confirm RED.
- [x] Implement schema/models/rules and section-reset helpers; re-run contract to GREEN.
- [x] Run `python scripts/verify-repo.py`, `python scripts/verify-structure.py`, and `python scripts/verify-csharp-lexical.py`.

### Task 2: Rollback-safe hotkey registry and action dispatch

**Files:**
- Modify: `src/Magic.Capture.App/Platform/HotkeyService.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Produces:** `TryRegisterPersonalHotkeys`, `PersonalHotkeyRequested`, settings-apply rollback, capture/workflow/MagicAction/editor dispatch.

- [x] Extend the contract with registry/rollback/dispatch markers and run RED.
- [x] Implement bounded dynamic Win32 registrations and rollback-safe `UpdateSettingsAsync`; dispatch targets through existing app entry points.
- [x] Run personalization + repository/structure/lexical + workflow-trigger verifiers.

### Task 3: Editor personalization and saved styles

**Files:**
- Modify: `src/Magic.Capture.App/Views/AnnotationWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Produces:** configured default/last-used tool, style preset load/save/delete, reorderable/hideable editor toolbar and overlay action layouts.

- [x] Add contract markers for startup tool precedence, best-effort last-tool persistence, style presets, toolbar/overlay order and visibility; run RED.
- [x] Implement editor/runtime/UI integration without changing project file payload semantics.
- [x] Run all existing static verifiers.

### Task 4: Monitor/app context preferences and section resets

**Files:**
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Produces:** monitor-specific overrides, executable-name app rules, Settings reset-by-section UI.

- [x] Add contract markers for active-monitor/app rule resolution and section-reset persistence; run RED.
- [x] Implement context resolution and UI CRUD with existing capture/profile services.
- [x] Run personalization + repository/structure/lexical verifiers.

### Task 5: Release truth and deterministic package

**Files:**
- Modify: `release/version.json`
- Modify: `release/feature-audit-660.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `README.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_4.15.0.md`
- Modify: `scripts/source-release.py`

- [x] Compare audit to the delivered 4.14.0 ZIP and prove only IDs 588-591 and 596-605 change to Done.
- [x] Set version 4.15.0 / 4.15.0.0 and update release documentation/checklist with hotkey conflict, rollback, reset and context-rule Windows gates.
- [x] Run all static contracts/verifiers and confirm 0 errors.
- [x] Run `python scripts/source-release.py` twice and confirm byte-identical provisional packages.
- [x] Extract provisional ZIP and rerun all verifiers/audit/version checks.
- [x] Mark packaging steps complete, create final A/B twice, verify byte-identical, then verify the exact delivered ZIP and SHA-256 sidecar.
