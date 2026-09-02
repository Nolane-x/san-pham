# Magic Capture Desktop 4.16.0 — Windows Release Checklist

This checklist is mandatory before calling the 4.16.0 source candidate a Windows release.

## A. Build / tests

- [ ] `python scripts/verify-repo.py` → 0 errors / 0 warnings.
- [ ] `.\scripts\test.ps1` → all xUnit tests pass.
- [ ] `.\scripts\build.ps1 -Configuration Release` → x64 build succeeds.
- [ ] ARM64 Release build succeeds.
- [ ] No new compiler warnings treated as unexplained release debt.
- [ ] XAML compiler succeeds for every window.
- [ ] `python scripts/verify-structure.py` → 0 errors.
- [ ] `python scripts/verify-csharp-lexical.py` → 0 errors.
- [ ] Feature ledger remains exactly 660 entries with 464 `Done`, 46 `Partial`, 92 `Foundation`, 36 `Missing` and 22 `ReleaseTest`; 4.16 promotes only #606–#608 and leaves #609 `Partial`.
- [ ] No bare `catch {}` blocks or unbounded whole-content file reads in production source.


## 4.16 Work Recovery gate

- [ ] Run `WorkspaceRecoveryPolicyTests` and verify Documentation/VideoEdit kind isolation, exact snapshot naming, age/future-skew boundaries, duplicate-session resolution and the eight-session cap.
- [ ] Build x64 and ARM64 Release, run the full xUnit suite, and XAML-compile Home plus `DocumentationWindow` and `VideoEditorWindow` with the new recovery lifecycle.
- [ ] In Documentation Builder, mutate title/subtitle/header/footer/template, steps, drag order and logo; wait at least 1.5 seconds, kill the process, relaunch, and verify the Home documentation recovery card restores the latest complete `.magicdoc` state including embedded images/logo.
- [ ] In Video Editor, mutate clips/segments/overlays/keyframes/audio/effects/output dimensions plus Undo/Redo; wait at least 1.5 seconds, kill the process, relaunch, and verify the Home video-edit recovery card restores the latest complete `.magicclip` state.
- [ ] Recover Documentation and Video Edit candidates and verify each opens as an unsaved copy with no original full path retained in its journal and no write to the original project until the user explicitly chooses Save.
- [ ] After a successful explicit save, relaunch and verify the matching recovery candidate is cleared only when no newer revision appeared while the save was in flight.
- [ ] Deliberately slow recovery writes, edit again, then save/open/close; verify revision + generation checks never let an older completion clear or replace newer recovery work.
- [ ] Close Documentation Builder and Video Editor normally with unsaved edits and verify their recovery journal/snapshot pairs are removed; exit the whole app instead and verify the latest completed snapshots remain discoverable next launch.
- [ ] Create more than eight sessions of each kind and verify pruning keeps only the newest eight valid Documentation and newest eight valid Video Edit sessions independently.
- [ ] Corrupt/oversize/expire/future-date journals; use wrong-kind extensions, mismatched session/revision names, missing snapshots and orphan snapshots; verify pruning fails closed and never touches ordinary `.magicdoc`/`.magicclip` files outside the dedicated recovery roots.
- [ ] Fault-inject termination after a new snapshot is fully promoted but before its journal is atomically replaced; relaunch must still offer the previous complete revision.
- [ ] Open a future-schema `.magicclip`; verify it remains read-only and does not produce a recovery journal.
- [ ] Confirm #609 remains `Partial`: interrupted recording detection is not promoted unless partial MP4 reconstruction/finalization is implemented and validated separately.

## 4.15.1 Quality Hardening gate

- [ ] Open two settings-mutating windows (for example Annotation + Pin/Settings), interleave saves, and verify the later mutation is applied to the newest snapshot rather than overwriting an unrelated earlier change.
- [ ] Force persistence failure after proposing new Region/Repeat/personal hotkeys; verify native registrations roll back. Force rollback failure too and verify the UI/log reports the rollback failure explicitly rather than only the storage error.
- [ ] Revoke Repeat entitlement while a Repeat hotkey is active, then force another desired hotkey conflict; verify rollback never restores the no-longer-entitled Repeat registration.
- [ ] Import a configuration with a temporarily locked/missing `settings.json` after archive commit; strict reconciliation must not normalize defaults and overwrite a valid imported file. The app must report restart/review guidance without claiming a clean apply.
- [ ] Import conflicting hotkeys and verify previous active hotkeys are retained, effective settings are reconciled back to disk when possible, and Settings UI reports the actual active/configured state.
- [ ] Import workflows/profiles that invalidate existing Workflow Triggers; dangling triggers are disabled in one bounded transaction, retained for repair, and the resident trigger engine reloads once.
- [ ] Delete a workflow referenced by RunWorkflow/ForEachImage/trigger, a Magic Action referenced by workflow/recipe, a Capture Profile referenced by trigger, a Local Action referenced by workflow/recipe, and a Custom Destination referenced by workflow/recipe; each delete is blocked before commit with dependency guidance.
- [ ] Simulate a prior cross-resource cleanup failure, restart, and verify catalog-aware startup reconciliation prunes stale workflow/Magic Action/profile hotkeys/default references before initial native hotkey registration without rewriting settings when no stale reference exists.
- [ ] Verify `ApplicationServices.Settings` has no public setter and child windows cannot write `SettingsStore` directly; all runtime mutation flows route through the serialized `App` authority.
- [ ] Windows CI restores Core tests separately and restores the WinUI app with `-p:Platform=x64` and `-p:Platform=ARM64` before corresponding `--no-restore` builds.
- [ ] Run all nine source verifiers, xUnit tests, XAML compilation, x64/ARM64 Release builds and MSIX packaging on Windows.

## 4.15 Settings & Personalization Runtime gate

- [ ] Upgrade settings schema v1 → v2 with an existing settings file; all old values survive and new personalization fields normalize to safe defaults. Future-schema recovery mode remains read-only until explicit reset.
- [ ] Register Personal hotkeys for Region, Foreground Window, Active Monitor, Virtual Desktop, Repeat and a `profile:<id>` Scrolling/fixed-region profile; verify 49th binding is rejected by normalization and duplicate/fixed/workflow-trigger conflicts fail visibly.
- [ ] Change only theme, toolbar order or a style preset while a valid hotkey is active; unchanged hotkeys are not needlessly re-registered. Force Windows to reject a proposed new hotkey and verify old Region/Repeat/personal registrations remain active and settings JSON is unchanged.
- [ ] Save workflow/Magic Action hotkeys with valid and missing IDs; missing targets fail before persistence and valid targets resolve again when the shortcut fires.
- [ ] Reorder/hide Editor toolbar and Capture overlay actions, close/reopen the relevant window and verify normalized order/visibility persists; malformed/duplicate/unknown action IDs are ignored without losing allowlisted defaults.
- [ ] Set a default annotation tool, disable last-used memory, then enable it and switch tools; reopen the editor after each case and verify precedence. Settings-storage recovery mode must prevent last-tool writes without breaking editor interaction.
- [ ] Create, update, apply and delete multiple named annotation styles; verify the 24-style cap, numeric clamps and no annotation-project payload/schema changes.
- [ ] Exercise Reset for Hotkeys, Capture, Output, Privacy, History, Personalization and Context Preferences; each reset changes only its section and preserves unrelated values.
- [ ] Configure two monitor overrides and test active/explicit monitor capture at mixed DPI. Device-name mismatch falls back to global settings.
- [ ] Configure a Per-app capture rule for a real `foo.exe`, verify both Region hotkey and Foreground Window capture route through the referenced profile after Magic Capture hides, and verify a deleted/missing profile prunes the rule during normalization. Paths/regex/UNC executable rules are rejected.
- [ ] Verify settings JSON contains only bounded configuration fields and no screenshot pixels, OCR/AI output, clipboard content, workflow values or provider secrets.
- [ ] x64 and ARM64 Release builds, xUnit tests and XAML compilation pass with the dynamic hotkey registry and 499+ Settings handlers.

## 4.14 History Intelligence & Organization gate

- [ ] Create, rename and delete Workspaces, one-level Folders and Collections; deleting organizers never deletes or relocates capture PNGs.
- [ ] Assign 1, 100 and multi-selected captures to workspace/folder/collections; invalid folder/workspace pairs are rejected and collection member cap fails explicitly rather than silently evicting another member.
- [ ] Lock `history-library.json` temporarily and verify History captures still open/list while organizer mutation fails without overwriting the valid file; corrupt JSON is quarantined separately.
- [ ] Delete/clear/retention History captures and verify organizer references are pruned best-effort after authoritative index commit without surfacing a late cancellation as a failed delete.
- [ ] Run workflows and Magic Actions from History and verify workflow filters record execution starts while AI-action filters include only actually-attempted steps, never skipped/dry-run-suppressed actions.
- [ ] Inspect `history-library.json`: no PNG bytes, OCR/AI text, prompt answers, HTTP bodies, clipboard data or Local Action stdout/stderr are present.
- [ ] Verify Most-used ordering with repeated opens/workflows and verify Timeline remains strictly newest-to-oldest even when List sort is Most used.
- [ ] Drag supported image files and folders containing 0, 1, 500 and >500 images; folder enumeration remains top-level only, deduplicates paths and enforces the 500-candidate cap.
- [ ] Window capture preserves process name + executable path when Windows allows access; denied/protected processes remain captureable with null metadata.
- [ ] App/process icons render best-effort, cache is capped at 256 PNGs / 2 MiB each, and UNC/network executable metadata is never dereferenced.
- [ ] Portable History export/import round-trips optional executable metadata and older History JSON without the new field still loads.
- [ ] List and Timeline multi-select continue to drive workflow batch, delete/export and Library-manager assignment consistently.
- [ ] x64 and ARM64 Release builds, xUnit tests and XAML compilation pass with the new manager window, drag/drop handlers, System.Drawing icon extraction and History query extensions.

## 4.9 documentation publishing gate

- [ ] Native **drag reorder** preserves the final step order; Move Up/Down remains usable by keyboard and pointer.
- [ ] `TemplateComboBox` exposes Clean, Compact, **presentation** and Print and each template visibly changes export geometry/typography without changing step content.
- [ ] Authored **header/footer** values round-trip through `.magicdoc` and render in HTML/Markdown/cards; DOCX contains and displays real header/footer parts in Microsoft Word and LibreOffice.
- [ ] Choose and Clear **logo** work for representative PNG/JPEG/BMP/TIFF inputs; saved projects reopen with the canonical embedded logo and projects with missing/unreferenced/oversized logo payloads fail closed.
- [ ] The generated **table of contents** preserves step order, emits a repeated section name only at the section boundary, and links to stable step anchors in HTML/Markdown/offline HTML.
- [ ] DOCX Contents ordering matches the project and the file opens without repair prompts in Word and LibreOffice.
- [ ] HTML folder, Markdown folder and offline HTML embed/reference only local assets and make no external requests.
- [ ] Long PNG and PDF render the template-aware overview card, header/footer and optional logo at 100/125/150/200% DPI source mixes.
- [ ] PDF with 511 steps stays within the page budget with its overview; PDF with 512 steps suppresses the extra overview and stays at the hard 512-page ceiling.
- [ ] x64 and ARM64 Release builds, xUnit tests and XAML compilation pass with the new drag events, template picker and logo picker code.

## B. MSIX / Store identity

- [ ] App associated with real Partner Center product.
- [ ] `store-preflight.ps1` passes.
- [ ] Package Identity/Publisher are production Store values.
- [ ] MSIX version equals `release/version.json` (`4.16.0.0`).
- [ ] x64 + ARM64 Store upload produced.
- [ ] Full-trust capability accepted.
- [ ] StartupTask and appExecutionAlias install correctly.

## C. Resident lifecycle

- [ ] Start-menu launch opens Control Center.
- [ ] Close button hides Control Center to tray.
- [ ] Tray icon remains responsive.
- [ ] `Win + Shift + X` remains active after Control Center closes.
- [ ] Only tray Exit terminates the app.
- [ ] Start-with-Windows login launch stays hidden.
- [ ] Second launch forwards to first instance and does not duplicate tray icon.
- [ ] CLI process forwards to resident process.
- [ ] Oversized single-instance pipe payload is rejected without resident memory growth.
- [ ] Command pipe accepts only the current Windows user.

## D. Capture / DPI

- [ ] Region capture on 100% DPI monitor.
- [ ] Region capture on 125/150/200% DPI monitor.
- [ ] Multi-monitor mixed-DPI layout.
- [ ] Negative-coordinate monitor layout.
- [ ] Foreground-window capture.
- [ ] Monitor capture.
- [ ] Full virtual desktop.
- [ ] Cursor on/off.
- [ ] WGC window capture succeeds on a supported Windows build with cursor on and off; cursor-off never silently includes the pointer.
- [ ] WGC monitor capture succeeds on representative 100/125/150/200% DPI outputs and reports exact physical-pixel dimensions.
- [ ] Force WGC unavailable/failure on a single-output cursor-off capture and verify routing attempts DDA before GDI.
- [ ] Verify DDA cursor-off accepts only frames whose DXGI metadata proves a separate visible pointer overlay; embedded/ambiguous cursor state must fall back to GDI.
- [ ] Cursor-on monitor/region capture never routes through DDA in 3.9.0 because pointer-shape composition is not implemented.
- [ ] Trigger/reproduce `DXGI_ERROR_ACCESS_LOST` via display-mode/desktop switch where possible; duplication is recreated at most once, then falls back while preserving the original failure kind.
- [ ] Device-removed/device-reset failure is classified, rebuilt at most once and falls back without hanging the resident process.
- [ ] Cross-monitor region and virtual-desktop capture remain GDI-only and preserve negative-coordinate physical bounds.
- [ ] Cancelling an in-flight WGC/DDA capture terminates the routing attempt and never starts a fallback capture.
- [ ] Backend diagnostics show attempted backend, elapsed time, failure kind and recovery count without exposing unbounded exception text.
- [ ] Delay 0/3/5/10.
- [ ] Fixed 1:1/16:9/4:3 Pro selection.
- [ ] Repeat-last-region Pro hotkey.
- [ ] Saved capture profile: region/window/monitor/desktop, cursor, delay, action.
- [ ] Profile Save honors PNG/JPEG/BMP/TIFF choice.
- [ ] Profile workflow selection runs the selected workflow with tier checks.
- [ ] Exact/recent region capture on negative-coordinate mixed-DPI desktop.
- [ ] Automatic scrolling capture reaches end without runaway input; cursor is restored.
- [ ] Horizontal scrolling capture moves right, detects end, performs bounded alignment correction and produces a seam-correct image.
- [ ] 2D scrolling capture succeeds on representative 2×2 and 3×3 scrollable canvases; page scroll position and cursor are restored best-effort afterward.
- [ ] 2D scrolling capture rejects a target that does not visibly move and never exceeds the 8×8 / 64-tile safety cap.
- [ ] 2D grid stitching tolerates a minority seam outlier when the remaining rows/columns agree, but fails closed without majority overlap consensus.
- [ ] Portrait + landscape mixed-DPI layout (including a negative X/Y monitor) preserves physical-pixel region bounds and overlay alignment.
- [ ] Force a transient GDI `CopyFromScreen` failure if reproducible; verify retry count is bounded to three and terminal error includes the physical-pixel rectangle.
- [ ] Region overlay HUD reports physical desktop X/Y/W/H on negative-coordinate mixed-DPI layouts.
- [ ] Eight post-drag resize handles preserve minimum size; reselect resets cleanly.
- [ ] Dark/light capture overlay preference persists and applies to new captures.
- [ ] Ellipse capture on 100/125/150/200% DPI produces correct alpha mask and global PixelBounds.
- [ ] Polygon capture: click vertices, Backspace/Undo, Enter finish, then action; invalid/collinear polygon stays in overlay with an error.
- [ ] Freehand capture remains responsive for long strokes and caps/simplifies samples without runaway allocation.
- [ ] Multi-region Canvas preserves relative positions and transparent gaps for 1, 2 and 16 regions.
- [ ] Multi-region Separate Images: Open adds all to History once; Save prompts for one folder and creates all files; Workflow runs once per image.
- [ ] Separate multi-region output rejects Copy/Pin/Edit/Text/Color/Magic instead of producing ambiguous one-image behavior or many windows.
- [ ] Automatic Scrolling overlay exposes rectangle only; non-rectangular shape buttons are unavailable.
- [ ] Shape selection on negative-coordinate monitor keeps desktop/global bounds correct.
- [ ] Shape switching/Reselect/Cancel releases overlay state and creates no resident worker/timer.
- [ ] UI Automation smart snap recognizes representative Win32, WinUI 3, WPF and Chromium controls without delaying normal drag capture when providers fail.
- [ ] Foreground-window z-order wins over controls from obscured windows.
- [ ] Rectangle drag and resize snap to nearby desktop/window/control edges, and disabling Smart Snap removes both target and edge snapping for that capture session.
- [ ] UIA snapshot on mixed-DPI and negative-coordinate monitors produces correct physical rectangles.
- [ ] Elevated/protected windows fail back to window/drag capture without hanging or crashing the resident process.
- [ ] Password edit controls never expose their value through CaptureAsset/ScreenGraph; OCR correlation is absent for password controls.
- [ ] UIA parent/child IDs remain valid after clipping a small capture out of a larger window.
- [ ] UIA ↔ OCR correlation links rendered button/edit text to the expected `wN` evidence IDs and remains bounded on OCR-heavy pages.
- [ ] Capture overlay startup latency is measured with Smart Snap on/off; UIA timeout fallback stays within the documented foreground latency budget.

## E. Deterministic recognition / imaging

- [ ] Windows OCR normal screenshot.
- [ ] Very large OCR screenshot downscales and returns source-aligned rectangles.
- [ ] OCR Preview word/line/block hit-testing aligns at 100/125/150/200% DPI and after scrolling/zooming the Result view.
- [ ] OCR search shows exact count below 256 and `256+` only when additional matches exist; clearing search removes all match rectangles.
- [ ] Plain/Layout/Code reconstruction preserves bounded output and Code indentation on representative source-code screenshots.
- [ ] Installed OCR language list is bounded/deduplicated; Auto and explicit language rerun are cancellable and closing Result cancels outstanding recognition.
- [ ] Windows language-pack link opens `ms-settings:regionlanguage`; missing language/provider failure leaves Result responsive.
- [ ] Table header/type inference matches integer/decimal/date/currency/percent fixtures for en-US and vi-VN cultures.
- [ ] Table anomaly diagnostics show row/column and expected→actual type without exposing unbounded cell text.
- [ ] CSV comma/semicolon, TSV and Excel-safe TSV preserve empty cells, locale conversion applies to the first data row, and formula-like text is neutralized.
- [ ] Oversized/malformed OCR tables hit extraction/serialization budgets cleanly without UI hangs or partial output.
- [ ] Table Workspace opens representative 1×1, 10×10, >64-row and >16-column tables without materializing more than one 64×16 page of cell buttons.
- [ ] Clicking cells changes active/range border in place without rebuilding the visible page; Extend selection visibly marks the selected range.
- [ ] Cell edit + 20-step Undo/Redo preserves the previous immutable table state.
- [ ] Insert/delete row and column update existing merge ranges deterministically and never leave a merge outside the table.
- [ ] Merge/unmerge hides merge followers while merged and restores their original underlying values after Unmerge.
- [ ] Copy selected cells preserves empty cells, tabs/newlines/quotes and rejects encoded TSV beyond 2,500,000 characters before unbounded growth.
- [ ] XLSX opens in Microsoft Excel and LibreOffice; multiline text, ampersands and merge ranges survive; text beginning with `=` remains text and never becomes a formula.
- [ ] Compare CSV/TSV handles quoted commas, doubled quotes, quoted newlines and empty cells; input >2 MB is rejected before full text allocation and diff output remains bounded.
- [ ] Tables over the editable 2,048-row / 128-column / 100,000-cell / 2,000,000-character budgets fail clearly instead of silently truncating.
- [ ] Text signal extraction for URL/email/path/error/stack.
- [ ] Table capture/serialization to CSV/TSV/Markdown/HTML/JSON.
- [ ] QR/barcode fixtures.
- [ ] PDF single-page, multi-page and contact-sheet exports open in Windows PDF viewers.
- [ ] JPEG target-size optimizer respects target/bounds across small and 4K fixtures.
- [ ] PNG lossless/lossy optimization produces readable files and never mutates the source capture.
- [ ] Data URI/Base64/file/path/folder clipboard actions paste into representative Windows targets.
- [ ] QR and Code 128 generator outputs decode with independent readers.
- [ ] Window/monitor test utilities close cleanly and add no resident worker after close.
- [ ] External editor launcher passes the selected image path as one argument with no shell interpolation.
- [ ] Effect pipeline brightness/contrast/gamma/exposure/saturation/grayscale/sepia/invert/posterize/threshold matches image fixtures.
- [ ] Multi-step effect order is deterministic and batch output matches single-image output for the same pipeline.
- [ ] Built-in effect presets do not mutate source History files.
- [ ] Speech balloon/callout text renders and remains editable after `.magiccapture` reopen.
- [ ] Numeric/alphabetic/Roman Step tools number deterministically.
- [ ] Cursor/click/emoji/magnify/spotlight/curved-line/curved-arrow/bracket tools render on PNG export.
- [ ] Editor tools and undo/redo.
- [ ] Layer duplicate/delete/z-order/show-hide/lock/rotation.
- [ ] Multi-select group/ungroup, align/distribute/equal-size and copy/paste preserve selection and group relationships.
- [ ] Editable layer X/Y/W/H, opacity, line style, fill and text style render correctly after project reopen.
- [ ] `.magiccapture` save/reopen preserves editable layers.
- [ ] Make an editor mutation, wait at least 1.5 seconds, kill the process without closing the editor, relaunch, and verify the Home recovery card offers the latest valid autosave.
- [ ] Recover an autosave and verify it opens in an editor without modifying or overwriting the original `.magiccapture` path.
- [ ] After a successful explicit project save, relaunch and verify no stale recovery card remains for that session.
- [ ] Close an editor normally and verify its LocalAppData recovery journal/snapshot are removed.
- [ ] Create more than eight recovery sessions and verify only the newest eight valid sessions survive pruning.
- [ ] Corrupt/oversize/future-schema/expired recovery journals and missing/mismatched snapshots are rejected and removed without touching ordinary user project files.
- [ ] Fault-inject termination after a new revision snapshot is promoted but before its journal pointer is promoted; relaunch must recover the previous complete revision rather than losing the session.
- [ ] Make another edit while a deliberately slow `Save Project` is still completing; after the save returns, the newer edit must retain/restart recovery instead of being cleared.
- [ ] Lock or temporarily deny access to a valid recovery snapshot, press Recover, then restore access; the transient failure must keep the candidate rather than deleting it.
- [ ] Keep two annotation editors dirty concurrently and verify autosave/prune in one session never removes the other session's current snapshot.
- [ ] Exit the whole application with unsaved annotation work and verify the last completed recovery remains available on the next launch; closing only that editor normally must remove it.
- [ ] A journal that points at another session or another dirty revision's `session-revision.magiccapture` snapshot is rejected.
- [ ] Malformed `.magiccapture` with NaN/huge points/duplicate IDs is rejected.
- [ ] Smart Redact produces editable local redaction layers.
- [ ] Blur/pixelate/highlight Plus/Pro gating.
- [ ] PNG/JPEG plus gated advanced formats.
- [ ] Pin resize/aspect/opacity.
- [ ] Pro click-through pin can always be recovered from tray.
- [ ] Vertical stitching.
- [ ] Deterministic Compare modes and metrics.
- [ ] Compare threshold/ignore-transparent/RGB metrics match fixtures.
- [ ] Heatmap/mask/blink/triptych modes render without timer leaks.
- [ ] Translation auto-align remains bounded to configured search range.

## F. History / utilities / workflows

- [ ] History write/read/reveal/delete.
- [ ] Retention behavior for Free/Pro.
- [ ] History text search.
- [ ] Corrupt history index is quarantined/rebuilt without deleting PNG files.
- [ ] History index traversal paths are rejected and cannot escape History root.
- [ ] Metadata/hash utilities.
- [ ] Beautify/combine/split/strip-metadata fixtures.
- [ ] Quick Copy workflow.
- [ ] OCR → Copy workflow.
- [ ] Documentation workflow.
- [ ] Data Capture workflow.
- [ ] Bug Report Pro workflow.
- [ ] Custom workflow validation rejects invalid/unsafe structure.

## F2. 4.11.0 Workflow Runtime v4

- [ ] Build x64 and ARM64, run the full xUnit suite including `WorkflowV4Tests`, and confirm WinUI XAML compilation for the parameter/trace controls and all 439+ handlers.
- [ ] Save/reopen/import/export schema-v4 workflows with Text, Choice and Boolean typed parameters; schemas 1–3 remain readable while v4-only fields/steps mislabeled as v1–v3 are rejected.
- [ ] Prompt Text accepts a bounded value, Prompt Choice only returns a declared choice, Confirm distinguishes Yes/No/Cancel, and missing interactive input fails cleanly in a non-interactive CLI path.
- [ ] Delay accepts 0 and 60000 ms, rejects out-of-range values, honors cancellation/step timeout, and a dry-run never sleeps.
- [ ] Build nested workflows to depth 4; a direct/indirect subworkflow cycle and a fifth level fail cleanly, while child tier checks remain enforced.
- [ ] Exercise a graph where the same child is first reached at the depth cap and later by a shallower path; Redact-before-Copy/Save/Pin must still detect guarded descendants through the shallower executable path.
- [ ] Run History batches with 1 and 500 captures; verify sequential dialog/clipboard semantics, lazy per-capture loading, cancellation, failed-load accounting and no retained image collection.
- [ ] Cancel/miss a required parameter before step 1 in single-run, batch and Studio dry-run; each started execution leaves a failed metadata trace without exception payload or capture/user value data.
- [ ] Inspect the newest-100 trace JSON after OCR, AI, HTTP, clipboard and Local Action workflows; trace privacy requires no image bytes, OCR/AI text, variables, HTTP bodies, clipboard payload, stdout or stderr.
- [ ] Dry-run workflows containing Copy, Save, Pin, Open Editor, HTTP, Magic Action, Local Action, prompts and Delay; all are reported as `WouldRun` and produce zero external/interactive side effects.
- [ ] Verify loop-over-images, resume/checkpoint, Task Scheduler, file watcher, clipboard/window/process triggers are not exposed as completed 4.11 features.

## F3. 4.12 Automation Triggers

- [ ] Build x64 and ARM64, run the full xUnit suite including trigger policy/CLI tests, and confirm WinUI XAML compilation with all 449+ handlers.
- [ ] Create/update/delete a Task Scheduler trigger and verify the task runs the packaged `magiccapture.exe --trigger <id>` alias for the current interactive user at LIMITED privilege without opening the Control Center.
- [ ] Expire/downgrade entitlement, then launch an existing scheduled task: history records/suppresses `feature_not_entitled`, the trial-expired dialog does not appear for the CLI launch, and resident trigger sources are absent.
- [ ] Change a Schedule trigger to another kind while intentionally preventing Windows task deletion; the stale task must be suppressed with `trigger_kind_mismatch` and cannot execute the new trigger kind.
- [ ] File watcher trigger: create/change/rename matching files, exercise recursion/filtering and a burst of changes, and verify one-pending-event + cooldown/circuit-breaker bounds. UNC/network-share paths must be rejected.
- [ ] Clipboard trigger fires from the Windows clipboard notification without persisting or inspecting clipboard contents for matching; a workflow that copies to clipboard must not create an unbounded self-trigger loop.
- [ ] Foreground-window trigger matches process/title case-insensitively, skips this app's own foreground events, and releases the WinEvent hook on reload/entitlement loss/Exit.
- [ ] Process-start trigger detects a newly started target within the two-second poll, ignores already-running processes at source startup, and creates no periodic timer when no process-start trigger is enabled.
- [ ] Hotkey trigger accepts A-Z/0-9/F1-F24 plus modifiers, reports OS registration collisions, caps workflow hotkeys at 16, and unregisters every id on reload/entitlement loss/Exit.
- [ ] Delete/rename referenced workflow or capture profile after a trigger was saved; the runner re-resolves current state and records `workflow_missing` / `profile_missing` rather than executing stale objects.
- [ ] Exact Region / Foreground Window / Active Monitor / Virtual Desktop profiles run unattended; interactive Region and Scrolling profiles fail closed for triggers while still working from the manual capture-profile path.
- [ ] Run more than 20 accepted attempts inside five minutes and verify a ten-minute circuit-breaker suspension; confirm cooldown begins after completion, not at start.
- [ ] Inspect newest-200 trigger history after all trigger kinds: it contains only id/name/kind/status/reason/timing metadata and no file names, clipboard text, foreground titles, command lines, pixels, OCR/AI text, HTTP bodies or Local Action output.
- [ ] Make trigger-history storage temporarily unwritable and run a successful automation; execution remains successful and only local logging reports the history persistence failure.

## G. Privacy outbound pipeline

- [ ] Redact-before-Copy never copies original pixels when enabled and redaction fails.
- [ ] Redact-before-Save never saves original pixels when enabled and redaction fails.
- [ ] Redact-before-Pin never pins original pixels when enabled and redaction fails.
- [ ] Redact-before-Workflow applies before configured outbound workflow paths.
- [ ] Pixelate and Blur redaction styles render locally.
- [ ] Custom sensitive words and bounded regex patterns survive settings restart.
- [ ] Invalid/overlong regex input is rejected or dropped without mutating into a different expression.

## H. Capture Watch

- [ ] Basic interval capture Free.
- [ ] Stop cancels promptly.
- [ ] Capture limit stops watch.
- [ ] Change-aware mode skips below-threshold frames.
- [ ] Change-aware mode triggers above threshold.
- [ ] Triggered workflow is tier-checked.
- [ ] UI-requiring steps are marshalled to UI thread.
- [ ] Cloud AI step requires confirmation; background Watch cannot silently upload.

## I. Custom Destinations

- [ ] HTTPS remote destination succeeds with test endpoint.
- [ ] HTTP remote destination rejected.
- [ ] HTTP localhost destination accepted.
- [ ] Secret placeholder resolves from PasswordVault.
- [ ] Plaintext sensitive Authorization header rejected by validator.
- [ ] GET/POST/PUT/PATCH cases tested.
- [ ] JSON body tested.
- [ ] Multipart image tested.
- [ ] Query/header/body templates tested.
- [ ] Result URL extraction tested.
- [ ] Oversized destination response rejected.

## I. AI endpoint / secret security

- [ ] Remote provider HTTP rejected at Save/Test/Request paths.
- [ ] HTTPS cloud provider accepted.
- [ ] localhost/127.0.0.1 HTTP accepted for local runtimes.
- [ ] Provider credential is in PasswordVault, not JSON profile.
- [ ] Delete provider removes credential.
- [ ] Logs do not contain API keys.
- [ ] `.magicaction`/`.magicrecipe` exports contain no secrets.

## J. Provider integration

For each provider advertised in the public Store listing:

- [ ] OpenAI Responses: model discovery, text, vision, structured response.
- [ ] Anthropic Messages: model discovery, text, vision.
- [ ] Gemini: model discovery, text, vision, JSON response mode.
- [ ] OpenRouter/OpenAI-compatible: models + chat-completions path.
- [ ] Ollama: `/api/tags`, text and compatible vision model.
- [ ] LM Studio: OpenAI-compatible local model.

Additionally:

- [ ] invalid credential error is clear;
- [ ] timeout can cancel;
- [ ] 429/temporary server response follows bounded retry policy;
- [ ] provider response > safety limit is rejected;
- [ ] OpenAI native request contains `store=false`;
- [ ] changing provider/model invalidates/rekeys AI cache appropriately.

## K. Small / large model behavior

- [ ] Text-only model receives ScreenGraph without image.
- [ ] Basic vision model receives downscaled image + ScreenGraph.
- [ ] Strong vision model receives intended image quality.
- [ ] Multi-image capability gates Context Stack image attachment.
- [ ] Never-send-cloud-images forces text/ScreenGraph path or cleanly rejects vision-required action.
- [ ] Prefer-local routing selects compatible local provider first.
- [ ] Best-capability routing selects compatible strongest profile.
- [ ] Active-only never silently switches provider.

## L. Magic Actions / evidence / context

- [ ] Deterministic recommender prioritizes error actions for error capture.
- [ ] Table capture recommends data actions.
- [ ] Generic text recommends general actions.
- [ ] Built-in Magic Actions execute.
- [ ] Custom `.magicaction` import/export/execute.
- [ ] Context Stack bounded size.
- [ ] `p:*` evidence highlights primary capture.
- [ ] `c1:*` evidence resolves context item and does not paint wrong primary pixels.
- [ ] Bad/unknown evidence ID is handled safely.
- [ ] Semantic Compare uses primary + comparison context.

## M. AI privacy / Guard / injection boundary

- [ ] Cloud dialog shows actual routed provider/model.
- [ ] Dialog summarizes image/context count.
- [ ] Local provider skips cloud-warning semantics.
- [ ] Local-only mode blocks cloud provider.
- [ ] Never-send-cloud-images works.
- [ ] AI Guard detects private-key header.
- [ ] AI Guard detects bearer token.
- [ ] AI Guard detects JWT-like token.
- [ ] Secret previews are redacted.
- [ ] Captured text that says “ignore previous instructions” remains inside untrusted-data boundary.
- [ ] Workflow/Recipe cloud AI cannot bypass confirmation.

## N. Magic Recipes / cache

- [ ] Create/edit/save/delete recipe.
- [ ] Import/export `.magicrecipe`.
- [ ] Recipe runs deterministic steps in order.
- [ ] Recipe calls built-in Magic Action.
- [ ] Recipe calls custom Magic Action.
- [ ] Invalid recipe rejected.
- [ ] Repeated identical AI request returns cache result without provider call.
- [ ] Changed model/action/context produces different cache key.

## O. Commerce

- [ ] Fresh install begins Plus without payment.
- [ ] Plus duration exactly 168h.
- [ ] clock rollback guard.
- [ ] Plus has no AI features.
- [ ] trial expiry → Free + one notice + no deletion.
- [ ] Pro purchase checkout.
- [ ] cancel checkout safe.
- [ ] successful Durable purchase → Pro Lifetime.
- [ ] cached Pro survives temporary Store failure/offline condition.
- [ ] localized current Store price.
- [ ] no Plus Store SKU.
- [ ] no subscription.

## P. Privacy / Store listing review

- [ ] Privacy policy updated for optional cloud AI and custom destinations.
- [ ] Listing does not say “no data ever leaves device”.
- [ ] Listing says cloud AI is user-configured and direct-to-provider.
- [ ] Listing says Pro does not include AI usage credits.
- [ ] AI Guard not marketed as complete DLP/security.
- [ ] Provider/model accuracy not guaranteed.
- [ ] Clean-room feature competition language does not imply source reuse.

## Q. Final release evidence

Record:

- exact commit/source hash;
- source ZIP SHA-256;
- Windows build logs;
- xUnit result count;
- x64/ARM64 package hashes;
- Store flight identity/version;
- tested OS builds/DPI layouts;
- provider/model IDs used for provider certification;
- Free/Plus/Pro entitlement test evidence.

## 3.2 Image Effects 2.0 manual matrix
- [ ] Apply every `ImageEffectKind` individually on transparent, opaque, tiny, 4K and 8K images.
- [ ] Chain 32 effects and confirm one decode/encode cycle and bounded memory behavior.
- [ ] Verify Hue/Vibrance/Color Balance preserve alpha.
- [ ] Verify Sharpen/Denoise/Edge/Mosaic on 1×1, narrow, portrait and alpha-heavy images.
- [ ] Verify torn/fade edges preserve untouched center pixels.
- [ ] Verify reflection orientation/fade and output dimensions.
- [ ] Verify all four border presets and arbitrary rotation with transparent/opaque backgrounds.
- [ ] Verify text/logo/date/capture-info watermarks at high DPI.
- [ ] Verify auto-crop does not crop meaningful content touching an edge.
- [ ] Verify transparency tolerance and exact color-key behavior.
- [ ] Import/export `.magiceffect`, reject oversized/corrupt/unknown-schema packs, and confirm imported pack preloads the pipeline dialog.

## 3.4 Pin Power UX manual gates

- Place Step and Note annotations at 100%, Fit, 25%, and 400% zoom; verify markers stay attached to the same image point.
- Test minimize/restore, edge-hide/restore and position lock on negative-coordinate secondary monitors.
- Close a pin while edge-hidden; reopen a pin and verify the remembered position is the pre-hide visible position.
- Arrange/snap 2, 4 and 12 pins across mixed-DPI monitors; verify locked pins are not moved.
- Pin clipboard images and local files near the import limit; verify oversized/huge pixel workloads fail before excessive allocation.


## 3.5 Design Tools manual gates

- [ ] Live picker samples correct pixels on 100/125/150/175/200% DPI and negative-coordinate monitors.
- [ ] Opening Ruler/Focus/Whiteboard stops the Design Tools sampling timer while the parent window is deactivated.
- [ ] Magnifier center pixel matches reported HEX/RGB; test at all virtual-desktop edges.
- [ ] History remains bounded to 32 and swatches to 24 across restart/settings recovery mode.
- [ ] HSV/CMYK/CSS/C#/C++ clipboard formats match the selected pixel.
- [ ] Palette average/dominant extraction behaves on transparent, uniform, noisy and 4K screenshots.
- [ ] WCAG ratios match known black/white and AA/AAA boundary cases.
- [ ] Ruler reports physical pixels and ΔX/ΔY correctly across mixed-DPI monitors.
- [ ] DPI calibration from a known physical reference persists for the open measurement session and rejects invalid values.
- [ ] Protractor angle is correct in all four quadrants.
- [ ] Screen Focus masks everything outside the selected rectangle without gaps.
- [ ] Whiteboard remains responsive for long strokes and respects the 8,192-point-per-stroke bound.
- [ ] Esc/Close releases overlay windows and no sampling/timer work remains afterward.

## 4.0 Visual Recording manual gates

- [ ] Build x64 and ARM64 with the pinned .NET 10 / Windows App SDK toolchain; run all xUnit tests.
- [ ] Record a region for 30 s at 5, 30 and 60 FPS; validate MP4 duration, dimensions and monotonically increasing playback timestamps.
- [ ] Record a moving/resizing window; movement may continue, but a dimension change must terminate cleanly instead of stretching frames.
- [ ] Record each monitor on mixed-DPI, negative-coordinate and portrait layouts; verify physical-pixel alignment.
- [ ] Record the full virtual desktop across mixed-DPI monitors; change desktop topology mid-recording and verify a clean terminal failure with `.partial.mp4` preserved.
- [ ] Verify cursor on/off through WGC and GDI paths; no stale cursor state between sessions.
- [ ] Verify `WDA_EXCLUDEFROMCAPTURE` keeps the Magic Capture recording card out of WGC/GDI/virtual-desktop output. Treat failure to apply display affinity as a blocked start.
- [ ] Pause for at least 30 s, resume, then verify the paused wall-time is absent from MP4 duration and no timestamp discontinuity causes playback failure.
- [ ] Test countdown values 0 and 10, manual stop, and automatic stop at the configured minute boundary.
- [ ] Test 25/50/75/100% scale on odd and even source dimensions; encoded dimensions must be even and >= 2 pixels.
- [ ] Test bitrate limits 1 and 50 Mbps and verify Windows rejects/handles unsupported encoder profiles cleanly.
- [ ] Verify H.264 MP4 with hardware acceleration requested on Intel, AMD and NVIDIA systems; record whether Media Foundation actually selects hardware or software encoding. Do not promote Hardware encoding from Partial without evidence.
- [ ] Force MediaTranscoder/codec failure; final `.mp4` must not be promoted and the `.partial.mp4` + journal must remain diagnosable.
- [ ] Kill the process during Preparing, Recording, Paused and Finalizing; next launch must report the unfinished journal without renaming the partial file.
- [ ] Place a future-schema recording journal and verify 4.0 treats it as read-only and never overwrites/deletes it.
- [ ] Record continuously for 30 min and 2 h at 1080p30; check memory, handle count, temp-file growth, thermal load and finalization time.
- [ ] Verify Store/MSIX packaged execution has permission to create Videos-library picker output and Windows Media transcode succeeds without external FFmpeg.

## 4.1 native recording-audio gate

- [ ] Build and run xUnit for the 4.1 tree; verify `RecordingAudioPolicyTests` and schema-v2 recording-manifest tests pass on Windows.
- [ ] Build/package x64 and ARM64 with `NAudio.Wasapi` 3.0.1 restored; launch on the minimum supported Windows 10 build 19041.
- [ ] Record video-only and confirm 4.0 behavior is unchanged and the MP4 contains no audio stream.
- [ ] Record system audio from the Windows default render endpoint and from an explicitly selected active render endpoint; verify AAC playback is continuous.
- [ ] Record microphone from the Windows default capture endpoint and from an explicitly selected microphone; verify correct channel/sample-rate output.
- [ ] Deny microphone privacy access, then request microphone recording; verify start fails cleanly with no silent downgrade and the UI explains that the requested source could not start.
- [ ] Record system audio + microphone together; exercise 0%, 100% and 200% independent gains and verify saturating mix does not wrap PCM samples.
- [ ] Leave loopback silent for at least 30 seconds, then resume playback; verify the video timeline does not shrink and the silent interval remains silent rather than causing an A/V jump.
- [ ] Pause for 30 seconds and resume; verify paused wall time is absent from both audio and video and post-resume A/V remains aligned.
- [ ] Record 10 minutes and 2 hours at 30/60 FPS with both audio sources; measure A/V drift at start, midpoint and end and record hardware/driver results.
- [ ] Unplug the selected microphone and disable/remove the selected render device during recording; requested-source loss must fail closed and preserve `.partial.mp4` plus schema-v2 recovery journal.
- [ ] Refresh audio devices after hot-plug and confirm endpoint lists/default markers update without restarting the app.
- [ ] Verify future schema 3+ recording journals are read-only and are never overwritten or deleted by 4.1.
- [ ] Inspect a completed A/V MP4 and verify H.264 video plus AAC 48 kHz stereo audio at the selected 96–320 kbps profile.


## 4.2 webcam / Picture-in-Picture gate

- [ ] Build and run xUnit for the 4.2 tree; `RecordingWebcamPolicyTests` and schema-v3 manifest tests pass on Windows x64 and ARM64.
- [ ] Camera permission: allow access and verify selected-camera initialization; deny camera permission and verify requested webcam recording fails cleanly with no final MP4 promotion.
- [ ] Test a machine with no camera; enabling Webcam/PiP must fail closed after the bounded warm-up rather than silently recording without PiP.
- [ ] Test integrated and USB camera devices at representative 720p and 1080p formats.
- [ ] Unplug the selected USB camera during recording; the session must fail, keep `.partial.mp4` + schema-v3 recovery journal and never reuse a stale webcam frame after terminal failure.
- [ ] Open the same camera in another application and verify SharedReadOnly behavior where the driver supports it; sharing failure is surfaced cleanly.
- [ ] Verify top-left/top-right/bottom-left/bottom-right and custom X/Y overlay placement at recording scales 25/50/100%.
- [ ] Verify 10/25/50% PiP widths remain fully inside 16:9, 16:10, ultrawide and portrait recording targets.
- [ ] Verify Rectangle, Rounded and Circle masks, mirror on/off, 20/50/100% opacity and 0/2/12 px border.
- [ ] Pause for 30 seconds with webcam active; resume without a 30-second timestamp gap and without camera reinitialization.
- [ ] Record video + system audio + microphone + webcam for 10 minutes and 2 hours; inspect A/V/webcam drift, memory growth and dropped/stale camera frames.
- [ ] Test 5/30/60 FPS recording with webcam enabled; the camera adapter must retain only one latest owned frame and must not grow an unbounded queue.
- [ ] Verify MediaFrameReference and SoftwareBitmap ownership under sustained recording; frame delivery must not stop due to exhausted frame-pool resources.
- [ ] Verify recording-control `WDA_EXCLUDEFROMCAPTURE` remains effective while webcam PiP is enabled.
- [ ] Place schema 1 and schema 2 journals containing legacy RecordingOptions and verify 4.2 reads them; place future schema 4+ and verify it is read-only and never overwritten/deleted.


## 4.3 recording effects + animated outputs gate

- [ ] Build x64/ARM64 and run all xUnit tests including `RecordingEffectsPolicyTests` and `AnimatedRecordingEncodingPolicyTests`.
- [ ] Start/stop 100 sessions with effects toggled; WH_MOUSE_LL/WH_KEYBOARD_LL hooks are removed every time and never swallow input.
- [ ] Verify plain unmodified A-Z/0-9 typing never appears in the safe-key overlay; Ctrl/Alt/Win shortcuts and function/navigation keys render correctly.
- [ ] Cursor highlight follows physical pixels on 100/125/150/200% DPI, negative-coordinate monitors and scaled recording output.
- [ ] Left/right clicks use visually distinct ripples and each ripple expires after the bounded lifetime with pause time excluded.
- [ ] Ctrl+Alt+drag drawing is written into the recording, remains bounded to 128 strokes / 2048 points per stroke and never intercepts the target app's mouse input.
- [ ] Ctrl+Alt+Z toggles 150/200/300% cursor-centered live zoom; webcam PiP remains fixed while screen content zooms.
- [ ] Record 10 min at 60 FPS with all effects enabled; inspect CPU/memory/handle growth and input latency.
- [ ] GIF: verify 5/15/30/60 FPS selections produce a standards-readable GIF89a with infinite loop, correct dimensions, non-empty frame sequence and deterministic global palette.
- [ ] APNG: verify Firefox/Chrome and an APNG-aware viewer play the output; validate acTL frame count, fcTL/fdAT sequence numbers and CRCs.
- [ ] Select GIF/APNG while audio controls were previously enabled; UI must clear/disable audio and service validation must reject any incompatible programmatic request.
- [ ] Kill the app during MP4/GIF/APNG recording; schema-v4 journal and matching `.partial.mp4`/`.partial.gif`/`.partial.png` stay untouched and are never promoted.
- [ ] Read schema 1-3 journals as legacy MP4; schema 5+ is read-only and never overwritten/deleted by 4.3.
- [ ] Animated WebP stays unclaimed/Partial until a local encoder is implemented and Windows playback/export evidence exists.

## 4.4 recording post-processing / clip-editor gate

- [ ] Build x64/ARM64 and run all xUnit tests including `VideoEditPolicyTests`.
- [ ] Open the Clip Editor from Control Center and add 1, 2 and 32 MP4 clips; no source probe may create an unbounded in-memory media copy.
- [ ] Save and reopen a `.magicclip` project; a FileSavePicker-created zero-byte placeholder is replaced atomically rather than rejected as corrupt JSON.
- [ ] Open a future-schema `.magicclip`; it is visibly read-only and 4.4 never overwrites it.
- [ ] Trim head/tail to frame-adjacent positions and verify `MediaTrimmingPreference.Precise` render duration against the project timeline.
- [ ] Cut a middle interval; verify the selected segment becomes exactly the non-empty left/right pieces and source time remains stable.
- [ ] Reorder, duplicate and combine clips from different source files; preview and final MP4 follow the exact timeline order.
- [ ] Mute a segment and test 50/100/200% volume; inspect the rendered audio for silence/attenuation/boost without changing neighboring clips.
- [ ] Apply normalized crop at each edge/corner and resize to odd/even requested dimensions; final H.264 dimensions remain even and crop never escapes the source canvas.
- [ ] Cancel MP4 render midway and force a render failure; final `.mp4` is never promoted and the same-directory hidden `.partial.mp4` is deleted best-effort.
- [ ] Render a successful MP4 over an existing FileSavePicker placeholder; final output is non-empty and playable.
- [ ] Capture frames at t=0, middle, final-tick and beyond EOS; output PNG is bounded and the requested time clamps inside composition duration.
- [ ] Build contact sheets with 1 and 64 frames; verify the 64-frame / 256 MiB BGRA hard caps before allocation and no thumbnail stream is retained after each cell.
- [ ] Keep #89 playback speed `Missing`; preview playback-rate controls must not be mistaken for a rendered speed transform.
- [ ] Keep #92 and #94–#99 `Missing` until audio extraction, title/text overlays, post-record zoom/blur/tracking and format conversion have end-to-end implementations.



## 4.5 advanced clip-editor gate

- [ ] Build x64/ARM64 and run all xUnit tests including advanced `VideoEditPolicyTests` and `VideoEditExportPolicyTests`.
- [ ] Open a schema-v1 `.magicclip`; verify it migrates in memory to schema v2, saves only as v2, and future schema 3+ stays read-only.
- [ ] Add, reorder, duplicate, remove, save and reopen title cards; verify title duration contributes exactly to project timeline duration.
- [ ] Add text/rectangle/ellipse/arrow overlays at timeline edges and canvas corners; verify normalized geometry, opacity and stroke bounds in preview and render.
- [ ] Verify overlay raster cache is content-addressed, remains within the 256-file / 64 MiB hard cap and survives preview/render long enough for MediaComposition to consume assets.
- [ ] Add a redaction overlay and verify solid-color redaction covers the requested region for its exact Delay/Duration.
- [ ] Auto-track a high-contrast moving target; verify base composition is sampled with overlays disabled, keyframes stay under 256 / 5 minutes and tracking stops on low confidence rather than jumping to a new target.
- [ ] Undo/redo an auto-track result and verify the original redaction geometry is restored exactly.
- [ ] Extract the composed timeline to WAV, MP3 and M4A after trim/reorder/mute/volume edits; verify extracted audio follows composed-timeline semantics, not the first source file.
- [ ] Convert the composed timeline to H.264 MP4, HEVC MP4 and WMV; if a codec is unavailable, `CanTranscode=false` must fail cleanly with no silent codec fallback.
- [ ] Cancel and force failures during extraction/conversion; hidden partial output must never be promoted to final output.
- [ ] Keep #89 playback speed `Missing`; preview playback rate is not a rendered speed transform.
- [ ] Keep #96 post-record zoom `Missing`; live recording zoom from 4.3 must not be mistaken for a post-production zoom effect.

## 4.6 editor retiming / frame-effects / audio-only gate

- [ ] Build x64/ARM64 and run all xUnit tests, including the 4.6 playback-rate/timeline-map/frame-effect and M4A policy regressions.
- [ ] Open schema-v1 and schema-v2 `.magicclip` projects; verify in-memory migration to schema v3, save as v3 only, and future schema 4+ remains read-only.
- [ ] Render representative segments at 0.25×, 0.5×, 1×, 2× and 4×; verify output duration follows rendered timeline while source trim/cut ranges remain unchanged.
- [ ] Mix different playback rates across multiple source segments and title cards; verify output-to-base timeline mapping has no boundary skips or repeated terminal frames.
- [ ] Render at 15/24/30/60 FPS and verify monotonically increasing timestamps, even output dimensions and the advanced-render frame/duration hard caps.
- [ ] Apply zoom/pan with two keyframes at canvas edges/corners; verify interpolation stays bounded and existing project overlays remain aligned after retiming.
- [ ] Apply Gaussian blur and pixelate at minimum/maximum strength to 720p/1080p/4K sources; verify bounded memory use and no out-of-canvas reads.
- [ ] Combine speed changes with system audio and microphone content; verify staged PCM is 48 kHz stereo/16-bit and output A/V duration remains aligned. Pitch preservation is not claimed in 4.6.
- [ ] Cancel or force a failure during advanced MP4 rendering; final output must never be promoted and staged PCM/partial MP4 files must be cleaned best-effort.
- [ ] Record M4A audio-only from system audio, microphone and both together; confirm no screen target picker appears and no screen frame is captured.
- [ ] Pause/resume M4A for at least 30 seconds; paused wall time must be absent from audio duration and recording journal audio-block progress must resume monotonically.
- [ ] Unplug/disable a requested audio endpoint during M4A recording; fail closed and retain the `.partial.m4a` plus recording journal schema v5 recovery metadata.
- [ ] Switch from MP4/webcam/effects to M4A; visual options must be cleared and disabled rather than leaving incompatible hidden state.
- [ ] Verify recording journal schemas 1–4 remain readable, schema v5 is current, and schema 6+ is read-only and never overwritten/deleted.


## 4.7 general timeline / keyframe gate

- [ ] Open `.magicclip` schema v1, v2, and v3 projects; confirm in-memory migration to schema v4 and successful save as v4.
- [ ] Open a synthetic schema v5 project; confirm read-only mode and no overwrite.
- [ ] Render overlay movement/opacity with Linear, EaseIn, EaseOut, EaseInOut and Hold; confirm generated overlay pieces stay <= 2,048.
- [ ] Validate rich title/text family, weight, italic, underline, alignment, shadow and outline on at least 720p and 4K output.
- [ ] Validate Fade In/Out + Duck and manual audio keyframes at 0.25x, 1x and 4x playback rate; inspect A/V alignment and clipping.
- [ ] Stress 128 audio-envelope keyframes, 256 overlay keyframes and 256 frame-effect keyframes; confirm bounded validation/failure.
- [ ] Undo/Redo title style, overlay keyframe, effect keyframe and audio-envelope operations.
- [ ] Compare a simple project against 4.6 output path; verify native fast path remains selected when no speed/frame-effect/audio-envelope is present.


## 4.8 step recorder / documentation gate

Historical **4.8.0** source gate retained for Step Recorder/privacy regression coverage.

- [ ] Build x64 and ARM64 and run the full xUnit suite, including `DocumentationPolicyTests`, `DocumentationArchivePolicyTests` and `DocumentationTextExportTests`; XAML compilation for `DocumentationWindow` succeeds.
- [ ] Plus/Pro `AdvancedWorkflows` gating opens the Documentation Builder; Free remains blocked without creating a recorder session.
- [ ] Start/Stop Step Recording 100 times. Verify the low-level mouse/keyboard hooks are **session-scoped hooks**, are removed after Stop/window close/application Exit, and no hook/timer/polling worker exists during ordinary tray idle.
- [ ] Verify left/right/middle click capture on Win32, WinUI 3, WPF and Chromium apps, including negative-coordinate and 100/125/150/200% mixed-DPI monitors.
- [ ] Verify safe keyboard shortcuts are labeled but ordinary printable typing (A-Z, 0-9, punctuation and Space without an allowed modifier/function/navigation role) is never persisted.
- [ ] Put keyboard focus in a UIA **password** edit control and trigger an otherwise-safe shortcut; no shortcut step, password value or sensitive UIA value is stored or logged.
- [ ] Force UI Automation timeout/provider failure; recording remains responsive and falls back to a bounded cursor-centered crop without crashing the tray host.
- [ ] Verify click/UIA target crops remain inside the monitor, the click marker aligns with the captured point, and burst duplicate clicks are coalesced within policy bounds.
- [ ] Exercise add image, move up/down, duplicate, merge-next, remove and per-step title/section/description edits; preview always matches the selected step.
- [ ] Save and reopen a `.magicdoc` with repeated image references and 512 steps; round-trip order, captions, target evidence and images are preserved.
- [ ] Reject `.magicdoc` traversal paths, duplicate ZIP entries, future schema, oversized manifest/image/aggregate payload and malformed PNG dimensions before unsafe allocation/extraction.
- [ ] Export a representative guide to long PNG and verify the explicit long-image pixel budget fails closed for oversized output.
- [ ] Export PDF and open it in two independent PDF readers.
- [ ] Export **DOCX** and open it in Microsoft Word and LibreOffice; text, ordering and embedded screenshots remain readable.
- [ ] Export HTML folder and Markdown + images into new dedicated output folders; failed promotion never replaces the selected parent folder.
- [ ] Export self-contained **offline HTML**, disconnect networking, reopen it in a browser and verify all screenshots/content render with no external requests.
- [ ] Force export/store write failures and cancellation where practical; no incomplete final file is promoted and staged/backup folders recover cleanly.
- [ ] Inspect `%LOCALAPPDATA%` logs after recording; no printable typed content, password values, or unbounded UIA text is present.

## 4.13 workflow control-flow / safe-resume gate

- [ ] Build x64 and ARM64 and run the full xUnit suite, including `WorkflowV5Tests`.
- [ ] Load schema-v1 through schema-v4 workflows unchanged; create/save a schema-v5 workflow containing `ForEachImage`; reject a v4 workflow mislabeled with the v5-only step.
- [ ] Select 1, 2, 8 and 32 History captures and run one workflow with them as the image loop; verify `loop.index`, `loop.number`, and `loop.count`, ordering, cancellation and resident-memory bounds.
- [ ] Attempt 33 loop images and `ForEachImage MaxAttempts=2`; both fail closed before executing child side effects.
- [ ] Test `continueOnError=false/true` with child workflows that return a failed result and child workflows that throw a non-fatal exception; only the true case proceeds to later images.
- [ ] Build parent→child→grandchild image-loop graphs and cycles; verify existing depth-4/cycle limits hold and nested loops see only their current image rather than multiplying the outer selection.
- [ ] Create a failed workflow after successful OCR/transform + Copy/Save/Pin/Open Editor steps; resume from its trace and verify pure state is replayed while those already-completed safe side effects are not repeated.
- [ ] Fail the resumed execution again, then resume the new trace; verify the cumulative safe-side-effect set survives and no previously suppressed Copy/Save/Pin/Open Editor action reappears.
- [ ] Change any execution-relevant workflow step/option/variable/parameter after a failure; resume must reject the stale SHA-256 fingerprint. Renaming/describing the workflow alone must not invalidate the execution-contract fingerprint.
- [ ] Delete or corrupt the source History image referenced by a failed trace; resume fails without deleting or mutating the trace.
- [ ] Fail a workflow in `RunMagicAction`, custom HTTP, Local Action, subworkflow or `ForEachImage`, and fail after one of those steps has succeeded; resume must be unavailable to avoid duplicated non-replayable effects.
- [ ] Inspect `%LOCALAPPDATA%` workflow traces after normal run, loop, failure and repeated resume; confirm no capture bytes, OCR/AI text, variables, prompt answers, HTTP bodies, stdout/stderr or Local Action output are present.
