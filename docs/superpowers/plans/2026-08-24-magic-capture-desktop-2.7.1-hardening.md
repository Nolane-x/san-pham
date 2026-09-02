# Magic Capture Desktop 2.7.1 Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and verification-before-completion. Preserve the 2.7.0 feature count; this wave is correctness/performance hardening, not feature inflation.

**Goal:** Harden the 2.7.0 source against long-running resident-state bugs, oversized inputs, UI-thread I/O stalls, image-memory spikes, and weak release checks without adding idle services.

**Architecture:** Put policy decisions in pure `Magic.Capture.Core` helpers, keep Windows/image implementation in `Magic.Capture.App`, and enforce critical invariants in release scripts. All new guards are on-demand and have zero idle CPU cost.

**Tech Stack:** .NET 10, WinUI 3, System.Drawing.Common, xUnit, Python release verifiers.

**Spec:** Existing comprehensive design plus the user's priority order: light, smooth, stable, correct UX.

## Global Constraints

- Product name remains exactly `Magic Capture Desktop`.
- No cloud account, hosted storage, analytics dependency, or developer-hosted AI.
- No new resident timer/background service.
- `Done` count remains 177 unless a pre-existing feature is demonstrably completed end-to-end.
- Windows build/xUnit/MSIX remains a mandatory external release gate because this environment has no Windows/.NET SDK.

---

### Task 1: Capture Watch baseline correctness
- Add pure trigger policy and tests.
- First sample establishes baseline when `OnlyWhenChanged=true`; it must not fire a false 100% change trigger.

### Task 2: Bounded generator input
- Add QR/Code128 input policy and tests.
- Reject oversized input before ZXing allocation/encoding.

### Task 3: Image workload safety
- Centralize encoded-byte, dimension, and pixel-area limits in `BitmapCodec.Decode`.
- Guard compare/effect/optimization paths consistently.

### Task 4: UI-thread history I/O
- Replace synchronous history image reads in navigation/context paths with async file reads.
- Preserve error logging and cancellation semantics.

### Task 5: Compare memory hardening
- Bound compare pixel area.
- Generate difference/heatmap/mask sequentially using one reusable map buffer instead of three full output arrays.

### Task 6: Capture Watch lifecycle
- Make stop/dispose idempotent, prevent post-dispose Start, and avoid stale completed-loop references.

### Task 7: Persistence and release hygiene
- Reject temp/editor backup artifacts in source releases.
- Remove stray `.tmp` files.
- Add verifier contracts for new safety rules and async history reads.

### Task 8: Full verification and 2.7.1 source snapshot
- Run repository verifier and structural verifier.
- Validate feature ledger stays exactly 660 with 177 Done.
- Build deterministic ZIP, test archive integrity, and write SHA-256.

### Task 9: Persistence health and crash consistency
- Settings recovery mode blocks unsafe automatic overwrites.
- History pending-add journal and backup-aware recovery protect interrupted commits.
- Atomic JSON persistence validates size/null roots and requires a safety backup before fallback overwrite.

### Task 10: Untrusted local configuration bounds
- Bound and validate workflows, destinations, Magic Actions, Magic Recipes and AI provider profile stores.
- Reject duplicate IDs and oversized collections/fields before publishing state or enabling writes.

### Task 11: Bounded local utilities and clipboard
- Directory Index streams enumeration and enforces entry/depth/name/output budgets before materialization.
- Base64/Data URI validates projected output size before conversion.
- Clipboard text preview uses bounded native Unicode access instead of full WinRT text materialization.

### Task 12: AI/provider/cache hardening
- Distinguish Credential Locker not-found from actual vault failures.
- Make AI disk cache bounded, best-effort and streaming-pruned.
- Bound provider model discovery to 512 unique, validated IDs.

### Task 13: Resident error boundaries
- Bound and rotate local logs.
- Remove bare catch blocks from production source.
- Centralize fatal exception classification and preserve fatal failures across workflow/startup/IPC boundaries.

### Task 14: Sequential PDF and large-batch memory control
- Emit PDF pages sequentially with a cumulative JPEG payload budget.
- Stream multi-page History PDF sources one at a time.
- Keep batch processing and Combine within resident-memory budgets.
