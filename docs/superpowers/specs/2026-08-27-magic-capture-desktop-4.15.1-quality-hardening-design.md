# Magic Capture Desktop 4.15.1 Quality Hardening Design

**Goal:** harden settings/runtime consistency without adding features or changing the 660-feature ledger.

## Architecture

`App` becomes the sole settings mutation authority. Mutations are functions of the latest in-memory snapshot and are serialized by one semaphore. Hotkey changes are applied transactionally before persistence; persistence is the commit point; post-commit effects are best-effort and explicitly masked. Child windows never write `settings.json` or assign the settings snapshot directly.

Configuration import is reconciled through the same authority. Strict settings reload is distinct from fail-soft startup loading. Cross-resource references are pruned or guarded: settings references are cleaned, destructive workflow/Magic Action/profile deletes are blocked when runtime dependencies exist, and imported workflow triggers that reference missing workflows/profiles are disabled rather than deleted.

Feature statuses remain identical to 4.15.0. Windows CI restores/builds the app per x64/ARM64 platform before using `--no-restore`.
## Additional hardened invariants

- Strict post-import settings load treats a missing settings file as failure; fail-soft defaults are reserved for startup recovery reads.
- Native hotkey unregister/register primitives remain private to `HotkeyService`; `TryApplyConfiguration` is the only runtime configuration surface.
- Repeat rollback is entitlement-aware and cannot resurrect a Pro shortcut after entitlement downgrade.
- Startup reconciles stale external workflow/Magic Action/profile references before initial hotkey registration, and performs no settings write when no repair is required.
- Workflow Trigger records whose imported workflow/profile target disappears are disabled in one store transaction rather than silently deleted.
- Destructive deletes are dependency-guarded for workflows, capture profiles, Magic Actions, Local Actions and custom destinations.
- Windows CI restore is platform-specific for the WinUI app so ARM64/x64 `--no-restore` builds use the corresponding restore graph.

