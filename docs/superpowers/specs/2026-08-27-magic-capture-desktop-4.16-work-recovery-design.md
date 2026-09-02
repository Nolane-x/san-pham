# Magic Capture Desktop 4.16 Work Recovery Design

## Goal
Extend crash-safe local work recovery from the annotation editor to every durable project-backed editor currently shipped by Magic Capture Desktop: Documentation Builder (`.magicdoc`) and Video Editor (`.magicclip`), while keeping recovery local, bounded, lazy, and non-destructive.

## Scope
This wave targets feature-audit rows #606 `Autosave editor project`, #607 `Restore editor after crash`, and #608 `Restore unfinished document`. Annotation recovery remains the existing reference implementation. Recording recovery (#609) is explicitly out of scope because finalizing a partially written media container requires a different media-recovery subsystem.

## Architecture
`Magic.Capture.Core` gains a shared `WorkspaceRecoveryPolicy` for journal schema validation, bounded candidate selection, deterministic revision-scoped snapshot names, extension/kind matching, age limits, and safe display-name handling. It contains no Windows or filesystem code.

`Magic.Capture.App` adds two small typed stores: `DocumentationRecoveryStore` and `VideoEditRecoveryStore`. They reuse the existing authoritative project writers/readers (`DocumentationProjectStore` and `VideoEditProjectStore`) rather than introducing permissive recovery parsers. Every autosave writes an immutable revision snapshot first and only then atomically replaces the session journal. The prior snapshot remains valid until the new journal pointer is durable, after which the older revision can be deleted.

`DocumentationWindow` and `VideoEditorWindow` each own a one-shot 1.5 second UI-thread debounce timer, a stable recovery session id, monotonically increasing dirty revision, generation token, and async write gate. No resident timer or background worker is introduced. Mutations schedule recovery; successful explicit save clears recovery only when no newer mutation appeared while the save was in flight. Opening another project invalidates and removes the prior recovery generation before replacing editor state. A normal user-initiated window close clears recovery; whole-app exit preserves the latest completed snapshot so an abnormal shutdown path remains recoverable.

Control Center Home surfaces separate local recovery cards for Documentation Builder and Video Editor alongside the existing annotation recovery card. Recover opens a copy of the autosaved work and never overwrites the original project path. Discard removes only the selected recovery session. Invalid/corrupt candidates are deleted/quarantined; transient I/O failures are retained for retry.

## Recovery journal and limits
- Journal schema version: 1.
- Recovery kinds: `Documentation`, `VideoEdit`.
- Maximum active sessions per kind: 8.
- Maximum journal size: 64 KiB.
- Maximum recovery age: 14 days.
- Maximum future-clock skew: 5 minutes.
- Maximum display-name length: 260 characters.
- Documentation snapshots: `<sessionN>-<20-digit-revision>.magicdoc`.
- Video snapshots: `<sessionN>-<20-digit-revision>.magicclip`.
- Journal file: `<sessionN>.json` inside the kind-specific recovery root.
- Journals store only kind, ids, timestamps, generated snapshot basename, dirty revision and optional display name. They never store original full paths, OCR text, AI content, image bytes, or video bytes.

## Documentation lifecycle
1. New Documentation Builder starts clean with a new recovery session.
2. Step capture/import, reorder, duplicate, merge, remove, metadata edits, logo/template changes and step text edits schedule a recovery autosave.
3. Autosave snapshots normalized project metadata plus currently referenced image/logo assets through `DocumentationProjectStore`.
4. Explicit `.magicdoc` save clears only the revision actually saved; a newer edit keeps recovery alive.
5. Opening a `.magicdoc` invalidates old recovery, installs the loaded package, starts a fresh clean session, and does not mark it dirty until the next edit.
6. Recovery opens the package into a new Documentation Builder while retaining the original recovery session/revision until the user saves or closes.

## Video Editor lifecycle
1. New Video Editor starts clean with a new recovery session.
2. Every successful project mutation through `CommitProject`, undo/redo, source/timeline edits, overlay/keyframe edits, effect edits, audio-envelope edits and output-dimension commits schedules recovery.
3. Autosave uses `VideoEditProjectStore.SaveAsync`; the recovery file references original media paths already present in the project model and does not duplicate media.
4. Explicit `.magicclip` save uses the same revision-aware clear rule as Documentation Builder.
5. Opening a project invalidates the old recovery before installing the loaded project and starts a fresh clean session.
6. A recovered project is revalidated through `VideoEditProjectStore.LoadAsync`. Future-schema projects remain read-only and are never autosaved by this build.

## Failure handling
- Snapshot write failure leaves the previous journal/snapshot pair intact.
- Journal promotion uses temp-file + atomic move semantics.
- Store operations are serialized per store so listing/pruning cannot race a snapshot promotion.
- Candidate load re-reads the journal and value-compares it with the discovered candidate before trusting its snapshot.
- Missing, unsafe, expired, oversized, mismatched-kind, mismatched-revision, or invalid-project candidates are not opened.
- Transient I/O/access failures do not delete a valid candidate.
- Clean-close cleanup is best-effort and never blocks application shutdown indefinitely.
- Cancellation is preserved; broad exception catches must not swallow `OperationCanceledException`.

## Verification
- Core xUnit tests cover safe snapshot names, wrong extension/kind, future timestamps, expiry boundaries, deterministic newest-first candidate selection, session cap, and display-name bounds.
- A dedicated source contract verifier checks both typed stores use the authoritative project stores, snapshot-before-journal promotion, bounded roots, timer/generation/write-gate window integration, Control Center recover/discard wiring, and no original full-path field in journals.
- Existing repository, structure, C# lexical, workflow, History and Settings verifiers remain green.
- Windows release checklist adds kill/relaunch, explicit-save race, open-project generation switch, clean-close removal, multi-session recovery and missing-media video recovery fixtures.
- This Linux source-generation environment cannot claim xUnit, WinUI/XAML compilation, MSIX, or native Windows runtime validation unless a Windows/.NET toolchain is available.
