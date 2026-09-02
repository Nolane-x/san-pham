# Magic Capture Desktop 4.15.1 — Quality Hardening

4.15.1 is a correctness and release-engineering patch. It promotes **no new feature** and keeps the 4.15.0 feature ledger unchanged at **461 Done / 46 Partial / 95 Foundation / 36 Missing / 22 ReleaseTest = 660**.

## Settings authority and lost-update protection

- `App` is the sole runtime settings mutation authority. Mutations are functions of the latest snapshot under one semaphore.
- `ApplicationServices.Settings` is read-only to consumers; only controlled commit can replace the resident snapshot.
- Annotation, Pin, Design Tools and MainWindow personalization/capture-profile paths no longer write settings storage directly.
- Persistence is the commit point; theme/retention/trigger/UI post-commit effects are best-effort and explicitly masked.

## Transactional global hotkeys

- Region, Repeat and personal hotkeys are applied as one Win32 transaction with active-gesture tracking and rollback reporting.
- Native unregister/register helpers are private so runtime callers cannot bypass the transaction.
- Rollback never restores Repeat when entitlement no longer permits it.
- Persistence failure plus rollback failure is surfaced as a combined serious error rather than hiding native-state divergence.

## Import and cross-resource consistency

- `.magicconfig` import uses strict post-commit settings reload; a missing/locked settings file is never silently normalized to defaults on the mutation path.
- Imported settings prune missing workflow, Magic Action and capture-profile references; conflicting imported hotkeys fall back to the previously active pruned set.
- Dangling workflow triggers are disabled, not deleted.
- Startup performs a catalog-aware stale-reference reconciliation before initial hotkey registration and writes only when repair is required.
- Workflow, Capture Profile, Magic Action, Local Action and Custom Destination deletes are dependency-guarded to avoid stranding workflow/recipe/trigger references.

## Verification and CI

- Added `verify-settings-consistency.py` and made source packaging require it alongside the seven existing gates.
- Windows CI runs all source verifiers, restores Core tests separately, then restores/builds the WinUI app per x64/ARM64 matrix platform.
- This source bundle is generated on Linux without the Windows/.NET toolchain; Windows xUnit, XAML, MSIX and native hotkey/runtime gates remain mandatory before binary release.
