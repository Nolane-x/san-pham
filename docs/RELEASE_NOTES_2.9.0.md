# Magic Capture Desktop 2.9.0 — UI Automation Capture Intelligence Source Release Notes

Magic Capture Desktop 2.9.0 adds bounded, on-demand Windows UI Automation intelligence to the capture fast path without introducing resident polling or per-pointer COM calls. It builds on the 2.8.0 capture-shapes checkpoint and keeps deterministic/local processing as the default.

## Capture UX

- Rectangle capture can snap to UI Automation controls captured before the overlay opens.
- Top-level window fallback and UIA control targets share z-order-aware hit testing so obscured controls cannot steal selection.
- Dragged/resized rectangle edges snap to nearby desktop/window/control edges within a small physical-pixel threshold.
- If UIA is unavailable or exceeds the capture-start latency budget, normal window/drag capture remains available.
- No UIA event subscription or background polling runs while Magic Capture Desktop is idle.

## UI Automation evidence

The bounded snapshot records control type, accessible name, AutomationId, value, enabled/checked/selected/focus state, bounding rectangle, parent relationship, process/window identity, access key and accelerator key. Snapshot limits are 384 accepted nodes, depth 10 and 12 top-level windows per active-monitor pass.

Password controls are fail-safe at two layers: the native acquisition path does not read their value, and Core normalization strips a value whenever `IsPassword=true` regardless of snapshot source.

## ScreenGraph correlation

Projected UIA nodes are carried on the in-memory `CaptureAsset` and merged into ScreenGraph. OCR correlation uses a bounded spatial grid rather than an unbounded UIA×OCR cross product. Semantic controls may receive bounded `ocrText`, `ocrWordIds` and `ocrWordCount` evidence; password controls never receive OCR correlation.

## Reliability / performance

- UIA acquisition runs on a dedicated MTA worker.
- Cache requests explicitly use Control View and batch property retrieval.
- Snapshot traversal has foreground and total traversal budgets plus node/depth/window/string caps.
- Rectangle capture remains the default fast path and UIA failures degrade to window snapping/normal drag rather than failing capture.
- UIA evidence is invalidated when a transform changes image dimensions.

## 660-feature ledger

This source snapshot reports **198 / 660 Done**. The 2.9 wave promotes exactly #12, #13, #24 and #529–#542. `Partial`, `Foundation`, `Missing`, and `ReleaseTest` remain incomplete.

## Verification boundary

Repository, structural and lexical source gates run in the generation environment. A real Windows machine is still required for .NET 10/WinUI compilation, xUnit execution, x64/ARM64 builds, MSIX packaging and UI Automation runtime/provider validation.
