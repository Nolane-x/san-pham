# Magic Capture Desktop 3.7 Data Resilience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make History, settings, configuration, and disposable AI cache durable, searchable, repairable, and portable without adding cloud services or unbounded I/O.

**Architecture:** Pure algorithms and validation live in `Magic.Capture.Core`; Windows/file-system integration stays in `Magic.Capture.App`. History PNGs remain the recovery source of truth while thumbnails, fingerprints and the text index remain derived/rebuildable state. Archive services are fail-closed, bounded, path-safe and explicit about excluded secrets/trust state.

**Tech Stack:** .NET 10, C# 14/latest, WinUI 3, System.Text.Json, System.IO.Compression, System.Drawing, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-3.7-data-resilience-design.md`

## Global Constraints

- Keep Magic Capture Desktop local-first and account-free.
- Do not export credentials, entitlement/trial state, local-action executable approvals, logs, AI result cache, or transient files.
- All archive reads are bounded before allocation and reject duplicate/path-traversal entries.
- Existing history/settings JSON remains readable.
- Heavy maintenance runs only on explicit user action or first on-demand search; no new idle timers/services.
- History correctness is more important than retaining derived caches; primary PNG files are recovery source of truth.
- Windows build/xUnit/MSIX remain external gates in this Linux environment.

---

### Task 1: History intelligence core

**Files:**
- Create: `src/Magic.Capture.Core/History/HistoryTextIndex.cs`
- Create: `src/Magic.Capture.Core/History/HistorySessions.cs`
- Create: `src/Magic.Capture.Core/History/HistoryDuplicateIndex.cs`
- Create: `src/Magic.Capture.Core/History/HistoryMaintenance.cs`
- Modify: `src/Magic.Capture.Core/History/HistoryItem.cs`
- Modify: `src/Magic.Capture.Core/History/HistorySearch.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistoryTextIndexTests.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistorySessionsTests.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistoryDuplicateIndexTests.cs`
- Test: `tests/Magic.Capture.Core.Tests/HistoryMaintenanceTests.cs`

**Interfaces:**
- Produces `HistoryTextIndex.Build/Search`, `HistorySessions.Summarize`, `HistoryDuplicateIndex.FindExact/FindNear`, `HistoryMaintenance.Plan`, and optional `HistoryItem.ContentSha256/PerceptualHash64`.

- [ ] Write tests that require the new APIs and verify source-level RED because the types do not yet exist.
- [ ] Implement bounded, deterministic pure helpers and normalize fingerprints.
- [ ] Run lexical/structural source gates and inspect tests/API signatures for consistency.

### Task 2: Fingerprints, indexed HistoryStore, health scan and repair

**Files:**
- Create: `src/Magic.Capture.App/Imaging/ImageFingerprintService.cs`
- Modify: `src/Magic.Capture.App/Persistence/HistoryStore.cs`
- Test: Core tests from Task 1 plus structural verifier contracts.

**Interfaces:**
- Produces `SearchAsync`, `GetSessionsAsync`, `GetDuplicateGroupsAsync`, `ScanHealthAsync`, `RepairAsync`, and fingerprinted new/recovered captures.

- [ ] Add source contracts for the new store API before implementation.
- [ ] Implement lazy text index invalidation/rebuild, SHA-256+dHash generation, health scan, repair and cancellation bounds.
- [ ] Ensure all writes still pass through the existing single-writer gate and atomic JSON/index path.

### Task 3: Portable archive policy and services

**Files:**
- Create: `src/Magic.Capture.Core/Portability/PortableArchiveModels.cs`
- Create: `src/Magic.Capture.Core/Portability/PortableArchivePolicy.cs`
- Create: `src/Magic.Capture.App/Persistence/ConfigurationArchiveService.cs`
- Create: `src/Magic.Capture.App/Persistence/HistoryArchiveService.cs`
- Test: `tests/Magic.Capture.Core.Tests/PortableArchivePolicyTests.cs`

**Interfaces:**
- Produces schema-1 `.magicconfig` and `.magichistory` manifests, bounded validation and import/export services.

- [ ] Write tests for future schema rejection, duplicate names, path traversal, exact allowlists, entry and byte limits.
- [ ] Implement pure manifest policy.
- [ ] Implement services using streamed ZIP reads/writes with no arbitrary extraction.
- [ ] Configuration import validates every payload before any destination file is committed; rollback restores previous generations on failure.

### Task 4: Settings persistence schema safety

**Files:**
- Modify: `src/Magic.Capture.Core/Settings/AppSettings.cs`
- Modify: `src/Magic.Capture.Core/Settings/AppSettingsRules.cs`
- Modify: `src/Magic.Capture.App/Persistence/SettingsStore.cs`
- Test: `tests/Magic.Capture.Core.Tests/AppSettingsSchemaTests.cs`

**Interfaces:**
- Produces `CurrentPersistenceSchemaVersion = 1`, normalized schema emission and future-schema recovery mode.

- [ ] Write schema tests before production changes.
- [ ] Add schema property and normalization.
- [ ] Probe schema from bounded JSON before normal settings deserialization and disable writes for future schemas.

### Task 5: AI cache maintenance

**Files:**
- Create: `src/Magic.Capture.Core/Ai/AiCacheMaintenancePolicy.cs`
- Modify: `src/Magic.Capture.App/Ai/AiResultCache.cs`
- Test: `tests/Magic.Capture.Core.Tests/AiCacheMaintenancePolicyTests.cs`

**Interfaces:**
- Produces a pure decision policy and `RepairAsync(maxAge, maximumEntries)` report.

- [ ] Write tests for invalid filename/key, oversize, expiry, future timestamp, entry cap and ancillary cleanup.
- [ ] Implement bounded cache scan/repair under the existing cache gate.

### Task 6: WinUI exposure

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Exposes Sessions, Duplicates, History Doctor, history archive import/export, configuration import/export, cache repair, and indexed search.

- [ ] Add buttons/handlers and source contracts first.
- [ ] Replace linear History search with debounced `HistoryStore.SearchAsync` while preserving final `HistorySearch.Matches` correctness.
- [ ] Add confirmation/status dialogs for repair/import and refresh UI after mutation.

### Task 7: Release contracts and 3.7 source bundle

**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/feature-audit/feature-backlog-660.json`
- Modify: `release/feature-audit-660.json`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Create/replace: `docs/RELEASE_NOTES_3.7.0.md`

**Interfaces:**
- Produces deterministic `Magic-Capture-Desktop-3.7.0-source.zip` and SHA-256 sidecar.

- [ ] Update source-truth audit only for capabilities actually implemented.
- [ ] Run `verify-repo.py`, `verify-structure.py`, and `verify-csharp-lexical.py` on the working tree.
- [ ] Run `source-release.py`.
- [ ] Extract the generated ZIP to a clean folder and run all three verifiers again on the packaged source.
- [ ] Check ZIP integrity, file count, version contract, audit ID continuity 1..660, and SHA-256.
