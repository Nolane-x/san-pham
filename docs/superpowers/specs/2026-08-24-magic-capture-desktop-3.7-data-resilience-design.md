# Magic Capture Desktop 3.7 Data Resilience & Portability Design

## Goal

Turn History and local configuration into durable, inspectable, portable product subsystems: indexed search, duplicate intelligence, deterministic repair, safe archive import/export, and forward/rollback-aware persistence without adding cloud services or resident background workers.

## Constraints

- Keep Magic Capture Desktop local-first and account-free.
- Do not export credentials, entitlement/trial state, local-action executable approvals, logs, AI result cache, or transient files.
- All archive reads are bounded before allocation and reject duplicate/path-traversal entries.
- Existing history/settings JSON remains readable.
- Heavy maintenance runs only on explicit user action or first on-demand search; no new idle timers/services.
- History correctness is more important than retaining derived caches. Primary capture PNG files are the recovery source of truth.
- Source bundle verification remains Linux/static in this environment; Windows build, xUnit, XAML runtime and MSIX smoke remain external release gates.

## Architecture

### 1. History intelligence core

Add pure Core helpers for full-text token indexing, session grouping, exact duplicate grouping, near-duplicate candidate indexing, archive manifests, and maintenance reports. `HistoryItem` gains optional backwards-compatible fingerprint fields: SHA-256 of the encoded PNG and a 64-bit dHash.

The text index is an in-memory inverted index built lazily from already-loaded local history metadata. Queries use AND semantics over normalized tokens, then the existing `HistorySearch.Matches` predicate remains the final correctness filter. This avoids a new SQLite/native dependency while still eliminating a full item scan for ordinary term queries.

Near-duplicate search uses eight 8-bit dHash bands. With a maximum supported Hamming threshold of 7, any valid near-duplicate pair must share at least one identical band, so candidate generation is sub-quadratic while final acceptance uses exact Hamming distance.

### 2. HistoryStore maintenance

`HistoryStore` computes fingerprints for new captures, exposes indexed search/session/duplicate methods, and provides explicit health scan/repair operations. Repair may:

- merge orphan primary PNGs back into the index as recovered items;
- remove rows whose primary file no longer exists;
- regenerate missing/stale thumbnails;
- populate missing content/perceptual fingerprints;
- remove orphan thumbnails;
- invalidate/rebuild the local text/OCR index.

All enumeration is bounded and cancellation-aware. Derived state can be rebuilt; primary images are never deleted by repair merely because metadata is missing.

### 3. Portable archives

Add two ZIP services:

- `.magicconfig` — allowlisted local configuration only: settings, workflows, destinations, local actions, custom Magic Actions and Magic Recipes. It explicitly excludes all secrets, approvals, commerce state, logs and caches.
- `.magichistory` — selected/all History metadata plus PNG payloads. Import validates every manifest row, entry name, entry count, individual image size and cumulative archive budget before calling HistoryStore to merge captures.

Both archives carry a schema-1 manifest with product name, source app version, creation time and payload inventory. Import is fail-closed for future archive schema versions.

### 4. Persistence schema safety

Settings files gain an optional `PersistenceSchemaVersion` field with current version 1. Runtime normalization always emits the current version. Settings load probes the raw JSON schema before deserializing: missing/0 is treated as legacy schema 0 and migrated in memory; versions greater than current enter recovery mode instead of being overwritten. This supplies explicit downgrade/rollback protection while keeping old files readable.

### 5. UI

History gets explicit actions for Sessions, Duplicates, History Doctor, Export archive and Import archive. Settings gets Export configuration and Import configuration. Destructive repair/import actions use confirmation dialogs and surface counts rather than silently mutating data.

## Error handling and security

- ZIP entry names are exact allowlists; no arbitrary extraction paths are used.
- No `ZipArchiveEntry.ExtractToFile` over untrusted names.
- All JSON/image reads have byte budgets and entry-count budgets.
- Import never copies credentials or local-action approval hashes.
- Invalid future settings schema causes read-only recovery mode.
- Partial history repair reports failures and preserves recoverable data.

## Testing

Add Core tests for token normalization/search, session grouping, exact/near duplicate grouping, archive policy, manifest validation, settings schema normalization, and history health planning. App-specific archive/UI behavior is covered by structural/source verifier contracts; full compile/xUnit/MSIX verification remains a Windows gate.
