# Magic Capture Desktop 3.7.0

Magic Capture Desktop 3.7.0 is a **data-resilience, History intelligence, and portability** release. The goal is not to add another cloud service or resident background agent; it makes the local-first capture library safer to search, inspect, repair, back up, and move between machines.

## History intelligence

- Added a bounded in-memory local token index for History metadata, OCR previews, barcode previews, tags, titles, notes, source/process/window/monitor/session metadata, dimensions, and dates.
- Control Center History search now queries `HistoryStore.SearchAsync` with cancellation/debounce, then applies the existing exact metadata/search predicates for correctness.
- Added SHA-256 fingerprints for exact duplicate detection.
- Added deterministic 64-bit dHash fingerprints and a bounded band index for near-duplicate discovery.
- Added session summaries with start/end time, capture count, total bytes, dominant process, and dominant source.
- Added History UI actions for Sessions and Duplicate Inspector.

## History Doctor and repair

History Doctor treats original History PNG files as the recoverable source of truth. Derived metadata is rebuilt rather than trusted blindly.

- Detects index rows whose primary PNG is missing.
- Detects recoverable orphan primary PNGs.
- Detects missing and orphan thumbnails.
- Detects missing SHA-256/dHash fingerprints.
- Removes unrecoverable missing-primary rows.
- Re-adopts valid orphan PNGs that follow the History storage naming contract.
- Rebuilds thumbnails using bounded image reads.
- Rebuilds fingerprints and the local text/OCR index.
- Removes orphan thumbnail files.
- Reports before/after health and bounded-operation failures instead of silently claiming success.

## Portable configuration bundles (`.magicconfig`)

Configuration export/import now uses an explicit manifest, SHA-256 inventory, entry-size budgets, cumulative archive budgets, safe ZIP entry names, and an allowlist.

Portable payloads can include:

- application settings and capture profiles;
- custom Magic Actions and Magic Recipes;
- custom workflows;
- custom HTTP destination profiles;
- Local Action profiles.

The following are deliberately excluded from portable configuration:

- API keys/provider credentials;
- destination credentials;
- Local Action executable approvals/hash trust state;
- Microsoft Store purchase/trial/entitlement state;
- logs;
- caches.

Imports are validated completely before commit. Configuration files are staged in memory within bounded limits, domain-validated, canonicalized where required, written atomically, and restored from transaction backups if a multi-file commit fails.

## Portable History bundles (`.magichistory`)

- Export selected captures or the complete indexed History up to the archive policy limit.
- Store PNG entries only under generated `images/{guid}.png` names.
- Include portable metadata such as title, notes, tags, favorite, source/process/window/monitor/session fields, OCR/barcode previews, dimensions, size, SHA-256, and perceptual hash.
- Validate manifest identity/schema, duplicate paths, traversal attempts, metadata/image inventory, dimensions, declared sizes, and SHA-256 before each image is accepted.
- Import images through bounded reads and `HistoryStore` rather than extracting arbitrary ZIP paths.
- Imported captures receive new local IDs, preventing archive IDs from overwriting existing History.
- Current History retention is reapplied after import.

## Settings schema and rollback safety

- Added `PersistenceSchemaVersion` to application settings.
- Legacy settings without the field are treated as schema 0 and normalized forward.
- Future/unsupported settings schemas enter safe recovery mode.
- In recovery mode, safe defaults are used for the session and automatic writes are disabled so an older build cannot overwrite settings created by a newer schema.
- Portable settings import rejects future schemas and canonicalizes readable settings to the current schema before commit.

## AI result cache repair

The AI result cache remains disposable and never becomes authoritative user data.

- Added a pure cache maintenance policy for SHA-256 file names, size limits, staleness, and future-clock-skew checks.
- Added explicit repair for malformed/oversized/mismatched entries.
- Removes expired entries and deterministic overflow beyond the configured cache size.
- Cleans atomic-write backup/temp residue with bounded scans.
- Reports when the scan limit is reached so cleanup can be continued safely.

## Reliability and security hardening

- Portable archive validation no longer constructs a throwing dictionary when a hostile manifest contains duplicate History entry names; invalid manifests return validation errors instead of crashing the validator.
- Archive reads use bounded stream helpers instead of unbounded allocation.
- Import code never trusts archive-provided extraction paths.
- History repair and AI cache repair are explicit user actions; 3.7.0 adds no resident repair worker or polling timer.
- Existing local-first and deterministic-first boundaries remain unchanged.

## Source-truth audit

The exact 660-feature ledger for 3.7.0 reports:

| Status | Count |
|---|---:|
| Done | 325 |
| Partial | 64 |
| Foundation | 142 |
| Missing | 107 |
| ReleaseTest | 22 |
| **Total** | **660** |

This promotes 13 source-backed items to `Done` in this wave, including near/exact duplicate detection, capture-session grouping, local FTS, profile/settings/History portability, cache repair, thumbnail rebuild, OCR-index rebuild, schema/rollback safety, and full configuration ZIP export.

## Validation boundary

This source-generation environment is Linux and does not contain the .NET SDK, Visual Studio, or Windows SDK. The repository/structure/C# lexical gates can be executed here, but **WinUI compilation, xUnit execution, MSIX packaging, and real Windows runtime smoke tests remain mandatory external release gates**. A static pass is not represented as a Windows runtime pass.
