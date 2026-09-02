# Magic Capture Desktop 4.15.1 Quality Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** eliminate settings lost updates, runtime/disk hotkey divergence, dangling configuration references, and weak Windows CI restore semantics without changing feature statuses.

**Architecture:** one `App` settings mutation gate; transactional native hotkeys; strict post-import reload; pure Core reference policies; fail-safe cross-resource delete/import reconciliation.

**Tech Stack:** .NET 10, C#, WinUI 3, JSON atomic persistence, Win32 RegisterHotKey, GitHub Actions Windows runners.

**Spec:** `docs/superpowers/specs/2026-08-27-magic-capture-desktop-4.15.1-quality-hardening-design.md`

## Global Constraints
- Feature ledger objects must remain identical to 4.15.0; only `sourceVersion` may change for 4.15.1.
- No new background service or cloud dependency.
- Existing fail-soft startup settings recovery remains intact.
- Child windows cannot directly persist or assign settings.

### Task 1: Regression contract and Core reference integrity
- [x] Add `verify-settings-consistency.py` and observe RED on 4.15.0.
- [x] Add Core reference policies and source tests.
- [x] Bring consistency contract toward GREEN.

### Task 2: Atomic hotkey configuration and settings mutation authority
- [x] Add all-or-nothing Region/Repeat/Personal hotkey configuration with rollback reporting.
- [x] Add read-only service settings snapshot and controlled commit.
- [x] Add serialized functional settings mutation authority with explicit effect mask.
- [x] Migrate child-window and MainWindow settings writes to the authority.

### Task 3: Strict import and cross-resource consistency
- [x] Add strict settings reload for post-import reconciliation.
- [x] Prune imported settings references to missing workflows/Magic Actions/profiles.
- [x] Disable dangling workflow triggers in one atomic store mutation.
- [x] Add deletion guards for workflow/Magic Action/capture-profile dependents.

### Task 4: CI and release verification
- [x] Restore/build Windows app per matrix platform in CI.
- [x] Run all source verifiers and preserve 660 feature rows.
- [x] Update version/docs to 4.15.1 without feature promotion.
- [x] Produce reproducible provisional A/B and verify extracted package.
- [x] Produce reproducible final A2/B2 and verify exact delivery ZIP/checksum.
