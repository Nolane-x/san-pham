# Magic Capture Desktop 4.9.0 — Documentation Publishing

Magic Capture Desktop 4.9.0 completes the five Documentation Builder capabilities that intentionally remained Foundation in 4.8.0. The wave stays local-first, introduces no cloud service or new network dependency, and keeps the `.magicdoc` schema at version 1.

## Documentation Publishing

- **drag reorder** — the step list supports native WinUI drag reorder, with Move Up/Down controls retained as an accessible deterministic fallback. The final visual order is validated against the project step IDs before it is committed.
- **page templates** — four stable profiles ship in `DocumentationTemplateCatalog`: Clean, Compact, Presentation and Print. The selected profile drives page/card geometry, spacing and typography across renderers.
- **header/footer** — projects expose authored header and footer metadata. HTML/Markdown/card outputs render them, while DOCX creates real `word/header1.xml` and `word/footer1.xml` package parts and relationships.
- **logo** — an optional local image is decoded, resized within the documentation logo budget, normalized to PNG, stored as canonical `logo.png` inside `.magicdoc`, validated on reopen, and embedded into local exports.
- **table of contents** — `DocumentationTextExport.BuildContents` produces stable ordered entries and anchors. HTML, Markdown, offline HTML and DOCX expose contents directly; long PNG/PDF receive a template-aware overview card.

## Export fidelity and bounds

All six existing export paths consume the same project publishing metadata: long PNG, PDF, DOCX, HTML folder, Markdown + images and self-contained offline HTML. Folder exports stage the optional logo alongside step images; offline HTML embeds it as a data URI and performs no remote request.

The existing PDF safety budget remains authoritative. A guide with fewer than 512 steps receives the overview/contents card plus its step pages. At exactly 512 steps, PDF suppresses the additional overview card so the output never becomes a 513-page document. Other formats keep their generated table of contents.

## Release truth

The 660-entry source audit is now **415 Done / 64 Partial / 122 Foundation / 37 Missing / 22 ReleaseTest**. Features #234, #239, #240, #241 and #247 move from Foundation to Done because their Core → UI → persistence/export wiring now exists in source. Autosave recovery (#254) remains Missing and is not claimed by this wave.

## Compatibility and verification boundary

There is no `.magicdoc` schema migration: schema version 1 remains current, and older 4.8.0 projects without template/logo metadata normalize safely to the Clean/default publishing behavior. No AI provider, server, API key or background documentation worker is required.

This source bundle is assembled in a Linux environment without the Windows .NET/WinUI toolchain. Repository, XAML-structure and C# lexical gates are run here; x64/ARM64 compilation, xUnit execution, XAML compilation, MSIX packaging and real Word/browser/PDF/runtime checks remain mandatory Windows release gates.
