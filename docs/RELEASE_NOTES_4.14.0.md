# Magic Capture Desktop 4.14.0 — History Intelligence & Organization

4.14 turns History from a flat capture list into a bounded local library while preserving the product's local-first durability model.

## New History organization

- Workspaces: up to 32 top-level groups.
- Folders: one level under a workspace, up to 128 total / 64 per workspace.
- Collections: many-to-many named sets, up to 128 collections, 5,000 members per collection and 32 memberships per capture.
- Organizer deletion never deletes the underlying capture.
- Organizer/activity metadata is stored separately in atomic `history-library.json` with a 32 MiB bound, backup/quarantine behavior and fail-soft reads.

## History intelligence

- Filter by workspace, folder or collection.
- Filter by workflow id applied from History.
- Filter by Magic Action id only when the corresponding workflow step was actually attempted; skipped and dry-run-suppressed actions are excluded.
- Sort List view by Most used using bounded local activity counters/timestamps.
- Timeline view is always chronological newest-first, independent of List sort.
- History open/workflow/AI activity persistence is best-effort and cannot make the primary operation fail.

## Drag/drop and process context

- Drop supported image files directly onto History.
- Drop a folder to import supported top-level images only, with a hard 500-file candidate cap.
- Window captures retain optional process/executable metadata through History and portable import.
- Process icons are best-effort local cache entries keyed by SHA-256-derived names, limited to 256 PNGs and 2 MiB per icon. UNC/network executable paths are rejected before icon resolution.

## Safety and compatibility

- Existing History `index.json` remains authoritative for captures; organizer corruption cannot delete capture rows.
- Transient organizer I/O is never treated as corruption and mutation paths do not overwrite a temporarily unreadable library.
- Post-delete organizer pruning runs only after the authoritative History index commit and is non-authoritative.
- Existing History JSON remains backward-compatible because `ExecutablePath` is optional.
- No screenshot pixels, OCR/AI text, prompt answers, HTTP payloads, clipboard contents or Local Action output are persisted in the History library.

## Verification boundary

The source-release gate runs repository, structure, C# lexical, workflow-trigger, workflow-control-flow and History-intelligence verifiers. Real WinUI compilation, xUnit execution, x64/ARM64 builds, MSIX packaging, drag/drop, process icon extraction and protected-process behavior still require the Windows release checklist.
