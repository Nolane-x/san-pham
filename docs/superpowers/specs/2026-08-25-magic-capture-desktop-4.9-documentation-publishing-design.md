# Magic Capture Desktop 4.9 Documentation Publishing Design

## Goal

Turn the 4.8 Documentation Builder from a strong recorder/editor into a complete local publishing workflow by finishing the five intentionally deferred capabilities: drag reorder, page templates, header/footer authoring, optional logo, and generated table of contents.

## Product constraints

- Stay fully local-first. No network services, telemetry dependency, account dependency, or remote asset loading.
- Keep Step Recorder hooks session-scoped exactly as in 4.8; this wave does not broaden input capture.
- Add no new NuGet dependency.
- Preserve `.magicdoc` schema version 1 because the model already carries `Header`, `Footer`, `LogoImageKey`, and `Template` fields.
- Keep archive/path/image size guards intact.
- Do not mark a feature Done merely because a model field exists; UI authoring and exported output must both be wired.
- The source bundle has no Git metadata in this environment, so implementation happens in the isolated extracted copy and is verified with project scripts.

## Design

### 1. Template catalog

Add a small deterministic `DocumentationTemplateCatalog` in Core. It exposes four stable template ids: `clean`, `compact`, `presentation`, and `print`. Each template defines bounded presentation properties used by renderers: page/card width, outer spacing, title/body scale, page orientation, margins, and a CSS class/token. `DocumentationPolicy.Normalize` canonicalizes unknown template values to `clean` so hand-edited or older manifests cannot inject arbitrary CSS/class names.

### 2. Authoring UI

Expand `DocumentationWindow` metadata controls with:

- Header text.
- Footer text.
- Template picker backed only by the four catalog ids.
- Choose logo / Clear logo actions and a short status label.
- Native ListView drag reorder (`CanDragItems`, `CanReorderItems`, `AllowDrop`) plus a completion handler that rebuilds `_project.Steps` from the final ListView order.

The existing arrow buttons remain as accessible deterministic alternatives. Choosing a logo decodes the selected image, bounds it, converts it to PNG, validates archive limits, and sets `LogoImageKey = "logo.png"`; clearing removes both the bytes and manifest reference.

### 3. Table of contents

Generate TOC content deterministically from the normalized step order. Sections appear once in first-seen order and steps always retain their numeric order. HTML uses local anchors, Markdown uses escaped anchor-compatible links, DOCX uses a static readable contents block, and image/PDF output gets a generated overview card. No dynamic scripting or external resources are needed.

### 4. Export fidelity

All documentation outputs consume the same project metadata:

- HTML: template class, optional logo file/data URI, document header, generated contents, anchored steps, footer.
- Markdown: optional logo reference, header block, contents, steps, footer.
- Offline HTML: same HTML with step images and logo embedded as data URIs.
- DOCX: title/contents/body plus real Word header/footer parts when authored; optional logo on the cover; template-controlled page size/margins.
- PDF and long PNG: overview card with title/subtitle/logo/contents, then template-aware step cards with authored header/footer and consistent spacing.

Logo bytes are passed separately from step images so `LogoImageKey` cannot collide with step assets.

### 5. Safety and compatibility

- Reuse `ImageFileReader`, `BitmapCodec`, and archive limits for logo import.
- Reject or normalize arbitrary template ids rather than interpolating them into output unsafely.
- HTML/XML/Markdown escaping remains mandatory for every user-authored field and generated TOC label.
- Folder exports stage all content before promotion; add `logo.png` only when a valid logo is present.
- Existing 4.8 `.magicdoc` files remain readable without migration because all affected manifest fields already existed and are nullable/defaulted.

## Verification

- Core tests cover template normalization/catalog, TOC ordering/escaping, HTML/Markdown metadata/logo output, DOCX header/footer/TOC package entries.
- Source contract verifier gains a 4.9 block requiring the new catalog, drag reorder UI/handler, logo import/clear flow, template authoring, and logo-aware exports.
- Structural verifier must resolve all new XAML handlers.
- C# lexical verifier must remain clean.
- Windows release checklist continues to require real WinUI drag/drop, mixed-DPI rendering, Word/PDF/browser opening, and MSIX build/runtime gates before binary release.
