# Magic Capture Desktop 4.8.0 — Local Step Recorder & Documentation Builder

4.8 turns the existing capture, UI Automation and export primitives into an end-to-end local documentation workflow without adding a cloud service or an always-running recorder.

## Highlights

- Explicit **session-scoped** Step Recorder. Hooks are installed only after Start Recording and are removed on Stop, window close and application exit.
- Local mouse-click observation for left/right/middle clicks with bounded duplicate-click coalescing.
- Keyboard observation is intentionally narrow: only safe shortcut labels are emitted; printable typed text is never buffered or persisted.
- UI Automation evidence is bounded and privacy-aware. Safe shortcuts prefer the focused control when available, and password controls suppress the shortcut step entirely.
- Deterministic click-area capture, UIA-aware crop planning, automatic step numbering/title/short description, and rendered click markers.
- Dedicated Documentation Builder with add/remove, move up/down, duplicate, merge, title/section/description editing and screenshot preview.
- Bounded `.magicdoc` ZIP project format with canonical-entry validation, traversal/duplicate rejection, future-schema refusal, per-image/total-size limits and save/reopen support.
- Six local exports: **long PNG**, PDF, **DOCX**, HTML folder, Markdown + images, and self-contained **offline HTML** tutorial.
- File exports use temporary-file promotion; folder exports use staged sibling directories with backup/restore behavior to avoid leaving half-written output.

## Source-truth audit

The 660-feature ledger is now **410 Done / 64 Partial / 127 Foundation / 37 Missing / 22 ReleaseTest**.

4.8 promotes Step Recorder/documentation rows only where the source is wired end-to-end. The following remain **Foundation** and are deliberately not claimed as complete: drag reorder (#234), page templates (#239), full header/footer authoring (#240), logo authoring/rendering (#241), and generated table of contents (#247).

## Safety / resource boundaries

- Maximum 512 documentation steps.
- `.magicdoc` manifests, image entries, aggregate image bytes and archive entry count are bounded before allocation/write.
- Imported images use bounded readers and dimension checks rather than unbounded `ReadAllBytes` calls.
- Long-image rendering has an explicit pixel budget.
- No resident Step Recorder timer, polling loop or input hook is started during normal tray startup.

## Verification boundary

The generation environment can run repository, structural, lexical, audit/version and packaged-source gates. It does **not** contain the Windows .NET/WinUI toolchain, so xUnit execution, XAML compilation, MSIX packaging and real input-hook/UI Automation/export interoperability remain mandatory Windows release gates in `docs/WINDOWS_RELEASE_CHECKLIST.md`.
