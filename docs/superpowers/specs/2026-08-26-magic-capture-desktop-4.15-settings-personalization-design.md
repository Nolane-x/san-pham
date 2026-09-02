# Magic Capture Desktop 4.15 Settings & Personalization Runtime Design

## Scope

4.15 closes the runtime gap for feature-audit items 588-591 and 596-605. It does not change pricing, add a service, or widen AI/network access.

## Architecture

`AppSettings` remains the single persisted authority. Schema v2 adds bounded personalization records: dynamic hotkey bindings, toolbar/overlay action layouts, annotation defaults and saved styles, per-monitor preferences, and per-app capture rules. `AppSettingsRules.NormalizeForRuntime` validates every identifier, action, tool, style and context rule before runtime use.

`HotkeyService` becomes a registry with fixed capture/repeat IDs plus bounded dynamic registrations. A dynamic binding resolves by kind at dispatch time: capture command, workflow ID, Magic Action ID, or editor command. Settings are persisted only after the new hotkey set can be registered; failed registration restores the previous runtime registration set.

Annotation personalization is local-only. Editor startup applies the configured default tool unless `RememberLastAnnotationTool` has a valid last-used tool. Tool changes persist best-effort. Saved styles contain presentation-only annotation values (color, stroke, opacity, fill, font fields) and never image pixels/text content.

Context preferences are deterministic. Monitor preferences key by normalized device name and can override cursor/post-capture action. Per-app rules match foreground executable file name only (not arbitrary regex), then select a capture profile and optional cursor/post-capture override. Invalid/missing profile references are removed by normalization.

## Bounds and safety

- persistence schema: 2, backward-compatible with schema 0/1;
- at most 48 dynamic personalization hotkeys;
- dynamic hotkey IDs/names/targets <= 128/160/128 characters;
- toolbar and overlay action IDs come from fixed allowlists, no arbitrary commands;
- saved annotation styles <= 24;
- monitor preferences <= 32;
- app capture rules <= 64 and executable name contains no path separators;
- hotkey keys support A-Z, 0-9 and F1-F24 with at least one modifier;
- settings storage recovery-mode protections remain authoritative;
- no persisted API keys, OCR text, AI prompts/results, pixels, clipboard contents, or process command lines are introduced.

## UI

Settings gains separate cards for Hotkeys, Toolbar & overlay, Editor personalization, Context preferences, and Section reset. Reorder operations use Up/Down buttons for predictable keyboard operation; visibility is explicit. Hotkey binding rows expose kind/target/gesture and registration status. Reset buttons restore only their section and preserve unrelated settings.

## Runtime integration

Capture hotkeys dispatch region/window/active-monitor/desktop/repeat. Workflow hotkeys load workflow by ID and capture region through the existing workflow entry path. Magic Action hotkeys capture region then open the existing Magic Action flow using the target action ID. Editor hotkey opens the main window History/editor affordance without synthesizing input.

Per-app capture rules are resolved before foreground-window capture; per-monitor preferences are resolved for active-monitor capture. Existing manual capture behavior is unchanged when no matching rule exists.

## Verification

Source-contract tests must first fail and then pass. Existing repository, structure, lexical, workflow-trigger, workflow-control-flow, and history-intelligence verifiers remain green. Final release truth promotes only audit IDs 588-591 and 596-605, then deterministic source packaging is run twice and the delivered ZIP is re-extracted and verified.
