# Magic Capture Desktop 2.9 UI Automation Capture Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded, pre-overlay UI Automation control snapping and ScreenGraph UIA evidence without resident polling.

**Architecture:** Native COM acquisition lives in the App and produces immutable Core snapshot records. Core performs normalization, hit testing, capture projection and OCR correlation; the overlay only performs in-memory hit testing, and `CaptureAsset` carries projected evidence to `ScreenGraphService`.

**Tech Stack:** .NET 10, WinUI 3, Win32 UI Automation COM, existing Magic.Capture.Core geometry/OCR/ScreenGraph.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-2.9-uia-capture-intelligence-design.md`

## Global Constraints

- No background UI Automation polling or event subscriptions.
- Snapshot before overlay activation.
- 384 nodes, depth 10, 12 top-level windows maximum.
- Existing rectangle/window snap path remains a fallback.
- No new cloud service or dependency.
- Windows build/runtime validation remains a release gate because this container has no Windows SDK/.NET compiler.

---

### Task 1: Core UIA snapshot rules

**Files:**
- Create: `src/Magic.Capture.Core/Capture/UiAutomationSnapshot.cs`
- Test: `tests/Magic.Capture.Core.Tests/UiAutomationSnapshotTests.cs`

- [ ] Write failing tests for bounded normalization, z-order-first hit testing, local coordinate projection, hierarchy preservation and duplicate-key handling.
- [ ] Run repository verifier to establish RED contracts.
- [ ] Implement immutable snapshot records and pure projection/hit-test helpers.
- [ ] Re-run verifier and structural/lexical gates.

### Task 2: Native UI Automation snapshot acquisition

**Files:**
- Create: `src/Magic.Capture.App/Capture/UiAutomationSnapshotService.cs`
- Create: `src/Magic.Capture.App/Platform/Native/UiAutomationInterop.cs`
- Modify: `src/Magic.Capture.App/Capture/WindowCaptureTarget.cs`
- Modify: `src/Magic.Capture.App/Capture/WindowCaptureService.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`

- [ ] Add contracts for MTA acquisition, explicit node/depth/window/string limits and recoverable provider failures.
- [ ] Preserve top-level z-order in the window catalog.
- [ ] Implement cached/controlled Control View traversal on a dedicated MTA worker.
- [ ] Register the service in the composition root and rerun all gates.

### Task 3: Overlay control snapping

**Files:**
- Modify: `src/Magic.Capture.App/Capture/CaptureCoordinator.cs`
- Modify: `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml`
- Modify: `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs`

- [ ] Snapshot UIA before constructing/activating the overlay.
- [ ] Pass monitor-local immutable snap targets to the overlay.
- [ ] Render control snap label/outline and choose UIA target before window fallback.
- [ ] Keep rectangle-only semantics for control snapping and preserve scrolling capture behavior.
- [ ] Run all static gates.

### Task 4: Carry UIA evidence into ScreenGraph

**Files:**
- Modify: `src/Magic.Capture.App/Capture/CaptureAsset.cs`
- Modify: `src/Magic.Capture.App/Capture/CaptureCoordinator.cs`
- Modify: `src/Magic.Capture.App/Ai/ScreenGraphService.cs`
- Modify: `src/Magic.Capture.Core/ScreenGraph/ScreenGraphModels.cs`
- Modify: `src/Magic.Capture.Core/ScreenGraph/ScreenGraphBuilder.cs`
- Test: `tests/Magic.Capture.Core.Tests/ScreenGraphUiAutomationTests.cs`

- [ ] Add projected UIA evidence to assets; invalidate on dimension-changing transforms.
- [ ] Pass asset UIA nodes into ScreenGraph build.
- [ ] Add accelerator key and OCR-overlap correlation attributes.
- [ ] Verify parent relationships remain valid after clipping/filtering.
- [ ] Run all gates.

### Task 5: Release contracts, ledger and Windows manual gates

**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `release/feature-audit-660.json`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_2.9.0.md`

- [ ] Add repository contracts for pre-overlay snapshot ordering, bounds, fallback and ScreenGraph wiring.
- [ ] Promote only feature IDs proven end-to-end.
- [ ] Run repository, structural and lexical verifiers on the versioned tree.
- [ ] Build deterministic source ZIP twice, verify identical SHA-256 and archive integrity.
