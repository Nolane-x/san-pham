# Magic Capture Desktop 2.7.1 — Hardening Source Release Notes

Magic Capture Desktop 2.7.1 is a **correctness, memory-safety, persistence-safety and resident-lifecycle patch** over 2.7.0. It intentionally does **not** increase the audited feature completion count: the ledger remains **177 / 660 Done**. The purpose of this release is to make the existing product foundation safer before feature development resumes.

## Major hardening changes

### Resident stability and cancellation

- Capture Watch uses the first sample as a baseline instead of reporting a false first-frame change.
- Capture Watch cancellation/disposal is idempotent and shutdown-safe.
- Compare recomputations cancel superseded work instead of merely discarding stale UI results.
- Translation auto-align uses bounded coarse-to-fine search with a minimum-overlap invariant.
- Fatal exception classification is centralized; OOM, stack overflow and native-corruption families are not swallowed by workflow/startup/IPC resilience boundaries.
- Local logs rotate at 8 MB per daily generation rather than growing without bound.
- Single-instance IPC is current-user-only, bounded to 64K characters and rejects oversized sender payloads before transmission.

### Image and memory safety

- Image file reads validate length before allocation.
- Pixel-processing, Compare and multi-selection workloads use separate memory/pixel budgets.
- Negative bitmap stride handling uses signed row offsets correctly.
- Blur uses fewer temporary buffers.
- Compare avoids intermediate normalized bitmaps and reuses map buffers.
- Batch effects, optimization and workflow execution stream History items one at a time.
- Combine preflights dimensions and decodes/draws inputs sequentially.
- Very large History captures can skip pre-generated thumbnails rather than forcing a huge full-image decode.
- Base64/Data URI copy validates projected output length before encoding.
- Clipboard text preview reads at most 16,000 Unicode characters without materializing the full clipboard text.
- PDF page emission is sequential and bounded; multi-page History PDF reads one source file at a time.

### Persistence and corruption recovery

- Settings fallback sessions are read-only until explicit recovery/reset; automatic state saves cannot overwrite unreadable settings.
- Explicit settings reset preserves old primary/backup files as recovery copies.
- Atomic JSON fallback refuses to replace an existing primary if a safety backup cannot be created.
- JSON roots, file sizes and collection counts are validated before exposing persisted state.
- History uses a pending-add journal to recover capture/index commits interrupted by crash or cancellation.
- History index recovery distinguishes corruption from transient I/O/permission failures.
- Missing primary History index can recover from a valid backup without losing metadata.
- History refresh is latest-wins and keeps the previous UI state when storage cannot be read safely.
- `.magiccapture` archives validate package size, entry count/names, duplicate entries, base image, annotations, OCR, tables and ScreenGraph payloads before use.
- Trial persistence with invalid/null/unknown state fails safe instead of silently starting a new trial.
- Workflows, destinations, Magic Actions, Magic Recipes and AI provider profiles have byte/count/duplicate-ID/field-length validation plus store-level write-health gates.

### AI / local integration safety

- PasswordVault “not found” is distinguished from genuine Credential Locker failures.
- AI result cache is best-effort, bounded and self-cleans corrupt/oversized/ancillary entries without turning a successful AI result into a failure.
- AI cache pruning uses bounded streaming priority selection instead of sorting the entire cache directory.
- Provider model discovery collects at most 512 unique model IDs, each at most 256 characters.
- Provider HTTP response bodies remain bounded before JSON parsing.

### Utility hardening

- Directory Index streams traversal and enforces entry/depth/name/output limits before materialization.
- QR/Code128 generator input is bounded before ZXing work.
- Exact resize rejects unsupported dimensions instead of silently changing requested output dimensions.
- External editor invocation remains no-shell and passes the image path as one argument.

## Release gates

The source-release script now requires all of these before creating a ZIP:

1. `scripts/verify-repo.py`
2. `scripts/verify-structure.py`
3. `scripts/verify-csharp-lexical.py`
4. ZIP integrity validation
5. deterministic file timestamps/order and SHA-256 generation

The feature ledger remains exactly **660 entries / 177 Done**.

## Verification boundary

This source bundle is generated in an environment without the Windows .NET 10 / WinUI toolchain. Static repository, structural and lexical gates are run here, but the following remain mandatory on Windows before calling 2.7.1 a production binary release:

- .NET/xUnit test execution;
- WinUI/XAML compilation;
- x64 Release build;
- ARM64 Release build;
- MSIX packaging/signing;
- mixed-DPI/multi-monitor runtime tests;
- Store entitlement/runtime smoke tests.
