# Magic Capture Desktop 3.6.0

## Local Actions: bounded local-tool automation

- Added persisted Local Action profiles for direct `.exe`/`.com` programs.
- Added explicit first-run approval pinned to canonical executable path + SHA-256. Replacing the binary invalidates the approval and requires a new confirmation.
- Added direct launch through `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`; Magic Capture does not invoke a command shell or perform shell interpolation by default.
- Added `$input`, `$output`, `$width`, `$height`, `$ocrText`, `$windowTitle`, `$source`, `$captureId` and workflow/CLI variable expansion.
- Added bounded execution timeout, stdout, stderr and output-file limits, with process-tree termination on timeout/failure.
- Added UTF-8 stdout/text output and PNG output chaining back into the workflow runtime. A returned PNG becomes the current image for later steps.
- Added on-demand OCR when a Local Action references `$ocrText` and no text value exists yet.
- Added a Control Center editor to create/test/delete Local Actions and explicitly revoke executable approvals.

## Workflow Studio

- Added a visual custom-workflow editor in the Workflows page.
- Added create/edit/duplicate/delete plus validated `.magicworkflow` import/export.
- Added drag/drop reordering and explicit up/down controls.
- Added backward-compatible per-step enable/disable; older workflow JSON that lacks the new field remains enabled by default.
- Added editing for required/optional behavior, argument, output key, condition, options, retry attempts/delay and timeout.
- Added bounded workflow default variables plus runtime CLI overrides.
- Added automatic minimum-tier inference for Studio-authored workflows that contain Pro-only AI or custom-destination steps.

## CLI and workflow runtime

- `--workflow <name>` now accepts repeated `--var name=value` arguments.
- Runtime variables are validated against reserved capture/runtime names before entering the workflow value map.
- Local Action stdout/stderr and chained output are available to downstream workflow steps.
- Redaction preflight ignores explicitly disabled Copy/Save/Pin steps.

## Source-truth audit

The exact 660-feature ledger moves from:

- Done: 288 → 312
- Partial: 79 → 70
- Missing: 122 → 107
- Foundation: 149 (unchanged)
- ReleaseTest: 22 (unchanged)

This promotes 24 audited workflow/Local Action capabilities to source-complete status. `Done` is a source implementation claim only; Windows compilation/runtime validation is still required.

## Verification boundary

This source-generation environment has no .NET SDK, Visual Studio, WinUI runtime or Windows SDK. The release therefore runs repository/static/lexical/XAML/ZIP gates here, but does **not** claim that xUnit, WinUI compilation, MSIX packaging or real Windows process/UI smoke tests were executed. Those remain mandatory release gates on Windows.
