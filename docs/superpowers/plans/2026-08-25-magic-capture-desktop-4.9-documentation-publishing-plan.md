# Magic Capture Desktop 4.9 Documentation Publishing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the five deferred Documentation Builder publishing capabilities end-to-end without adding cloud services or dependencies.

**Architecture:** Keep `.magicdoc` schema 1 and add a deterministic Core template catalog plus shared TOC generation. Extend the existing WinUI Documentation Builder and each exporter to consume the same normalized metadata, passing logo bytes separately from step images.

**Tech Stack:** .NET 10, C#, WinUI 3, System.Drawing, Open XML package generation via `System.IO.Compression`, Python source verifiers.

**Spec:** `docs/superpowers/specs/2026-08-25-magic-capture-desktop-4.9-documentation-publishing-design.md`

## Global Constraints

- Fully local-first; no network or remote asset loading.
- No new NuGet dependency.
- Preserve `DocumentationProject.CurrentSchemaVersion = 1`.
- Preserve existing archive/path/image safety limits.
- Keep Step Recorder hooks session-scoped; this wave must not broaden keyboard capture.
- Only mark #234, #239, #240, #241, #247 Done after Core + UI + export + verifier wiring exists.

---

### Task 1: Template catalog and deterministic TOC model

**Files:**
- Create: `src/Magic.Capture.Core/Documentation/DocumentationTemplateCatalog.cs`
- Modify: `src/Magic.Capture.Core/Documentation/DocumentationPolicy.cs`
- Modify: `src/Magic.Capture.Core/Documentation/DocumentationTextExport.cs`
- Test: `tests/Magic.Capture.Core.Tests/DocumentationPolicyTests.cs`
- Test: `tests/Magic.Capture.Core.Tests/DocumentationTextExportTests.cs`

**Interfaces:**
- Produces: `DocumentationTemplateCatalog.NormalizeId(string?)`, `Get(string?)`, `DocumentationTextExport.BuildContents(DocumentationProject)`.
- Consumers: WinUI metadata picker and all renderers/exporters.

- [ ] Add failing tests proving unknown template ids normalize to `clean` and all four supported ids round-trip.
- [ ] Run the Core test project on Windows/.NET when available; in this environment add verifier contracts before production changes so the source gate is red.
- [ ] Implement the bounded catalog and wire `DocumentationPolicy.Normalize` to it.
- [ ] Add failing export tests for deterministic contents ordering and escaping.
- [ ] Implement shared TOC generation and anchor helpers without external dependencies.
- [ ] Run repository, structural, and lexical verifiers.

### Task 2: Metadata authoring and drag reorder UI

**Files:**
- Modify: `src/Magic.Capture.App/Views/DocumentationWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs`

**Interfaces:**
- Consumes: `DocumentationTemplateCatalog` ids and existing `DocumentationProject` metadata.
- Produces: header/footer/template/logo authoring and `StepList_DragItemsCompleted` ordering synchronization.

- [ ] Add verifier expectations for template picker, header box, logo choose/clear handlers, and native ListView reorder properties/handler; confirm verifier fails.
- [ ] Add WinUI metadata controls while preserving existing keyboard-accessible move buttons.
- [ ] Implement logo import using `ImageFileReader` + `BitmapCodec`, bound to archive limits, and clear flow.
- [ ] Implement drag completion by rebuilding `Steps` from current ListView items and touching project metadata.
- [ ] Wire `SyncProjectMetadata`/`LoadProjectMetadata` to header/footer/template/logo state.
- [ ] Run all static verifiers.

### Task 3: HTML, Markdown, and offline publishing fidelity

**Files:**
- Modify: `src/Magic.Capture.Core/Documentation/DocumentationTextExport.cs`
- Modify: `src/Magic.Capture.App/Documentation/DocumentationExportService.cs`
- Test: `tests/Magic.Capture.Core.Tests/DocumentationTextExportTests.cs`

**Interfaces:**
- Produces: logo-aware `BuildHtml`, `BuildMarkdown`, and `BuildSelfContainedHtml` overloads; folder export writes `logo.png` atomically inside staging.

- [ ] Add failing tests for header, contents anchors, template class, footer, and logo references/data URI.
- [ ] Implement escaped metadata + TOC output in HTML and Markdown.
- [ ] Extend offline HTML to embed logo bytes using the same archive bound checks.
- [ ] Extend folder staging to write `logo.png` only when provided and update UI export calls to pass `_logoPng`.
- [ ] Run all static verifiers.

### Task 4: DOCX, PDF, and long-image publishing fidelity

**Files:**
- Modify: `src/Magic.Capture.Core/Documentation/DocumentationDocxWriter.cs`
- Modify: `src/Magic.Capture.App/Documentation/DocumentationCardRenderer.cs`
- Modify: `src/Magic.Capture.App/Documentation/DocumentationExportService.cs`
- Test: `tests/Magic.Capture.Core.Tests/DocumentationTextExportTests.cs`

**Interfaces:**
- Produces: DOCX header/footer parts and static contents block; template-aware overview/step card rendering; logo-aware binary exports.

- [ ] Add failing DOCX package assertions for contents text and header/footer parts.
- [ ] Implement template-driven DOCX page geometry and actual header/footer package parts; add cover logo when provided.
- [ ] Add an overview card renderer containing title/subtitle/logo/contents.
- [ ] Make step cards consume template geometry and authored header/footer.
- [ ] Make long PNG/PDF prepend the overview card and pass logo through the export service.
- [ ] Run all static verifiers.

### Task 5: Release truth and source package

**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/FEATURE_MATRIX.md`
- Create: `docs/RELEASE_NOTES_4.9.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Modify: `README.md`
- Modify: `release/version.json`
- Modify: `release/feature-audit-660.json`
- Modify: packaging/version source files referenced by the repository verifier.

**Interfaces:**
- Produces: source version `4.9.0`, MSIX version `4.9.0.0`, and feature-audit truth where #234/#239/#240/#241/#247 are Done only after tasks 1-4 are source-wired.

- [ ] Extend the verifier with a 4.9 contract and expected audit counts; make it fail before release metadata changes.
- [ ] Update feature audit rows and regenerate synchronized audit JSON.
- [ ] Update release notes, matrix, README, version metadata, and Windows-only runtime checklist.
- [ ] Run repository + structural + lexical verifier fresh from the final tree.
- [ ] Build deterministic source ZIP twice, compare SHA-256, test ZIP integrity, extract it, and rerun all three verifiers from the extracted package.
