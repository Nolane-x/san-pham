# Magic Capture Desktop 4.15.0 — Settings & Personalization Runtime

4.15 closes the remaining runtime gaps in the Settings/personalization cluster without adding a background service or cloud dependency.

## Global hotkeys

- Up to 48 personal hotkeys for capture actions or `profile:<id>` capture profiles, workflows, Magic Actions and opening the latest History capture in the editor.
- Workflow and Magic Action targets must exist before a binding is saved.
- Dynamic `RegisterHotKey` application is rollback-safe: Windows registration succeeds before settings persistence; a failed proposal restores the previous Region/Repeat/personal set.
- Unrelated settings changes do not re-register unchanged hotkeys.

## Editor and overlay personalization

- Reorder or hide allowlisted Annotation toolbar actions and capture-overlay primary actions.
- Choose a default annotation tool or remember the last-used tool.
- Save/apply/delete up to 24 local named annotation styles with bounded numeric/font fields.
- Reset Hotkeys, Capture, Output, Privacy, History, Personalization or Context Preferences independently.

## Context-aware capture

- Per-monitor cursor/post-capture overrides for active or explicitly chosen monitor capture.
- Per-app executable-name rules select an existing capture profile and optional cursor/post-action overrides when Region or Foreground Window capture is invoked.
- App rules store a file name such as `foo.exe`, not a path/regex, and invalid profile references are pruned by normalization.

## Safety and compatibility

- Settings persistence schema advances to v2 while continuing to read v0/v1 data.
- Personalization stores configuration only; no image, OCR/AI, clipboard or workflow-result payload is added to settings.
- Static release gates include the dedicated Settings personalization verifier. Real WinUI compilation, RegisterHotKey behavior, mixed-monitor rules, x64/ARM64 builds and MSIX packaging remain Windows release gates.

Feature ledger: **461 Done / 46 Partial / 95 Foundation / 36 Missing / 22 ReleaseTest = 660**.
