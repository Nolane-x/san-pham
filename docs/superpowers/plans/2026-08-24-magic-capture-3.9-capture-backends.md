# Magic Capture Desktop 3.9 Multi-Backend Capture Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic WGC → Desktop Duplication → GDI capture router with bounded GPU recovery and truthful diagnostics.

**Architecture:** Pure Core policy decides candidate order from target/cursor/capabilities. App backends implement WGC, single-output Desktop Duplication, and GDI behind one interface, while `ScreenCaptureService` becomes a router facade. GPU resources are isolated in backend classes so fallback remains safe when WGC/DXGI fail.

**Tech Stack:** .NET 10, WinUI 3, Windows.Graphics.Capture, Windows.Graphics.Imaging, Vortice.Direct3D11 3.8.3, Vortice.DXGI 3.8.3, System.Drawing.Common 10.0.0, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-3.9-capture-backends-design.md`

## Global Constraints

- Preserve physical-pixel coordinates and existing `ImageWorkloadLimits`.
- Auto ordering: Window WGC→GDI; Monitor/one-monitor Region WGC→DDA→GDI; Virtual/cross-monitor GDI only.
- Desktop Duplication is excluded when cursor capture is requested in 3.9.
- WGC first-frame timeout ≤ 1500 ms.
- DXGI acquisition timeout ≤ 1000 ms and recreation budget = 1.
- GDI retry budget remains exactly 3 attempts / 40 ms delay.
- No HDR/10-bit audit promotion in this release.
- Windows runtime/build claims remain external until tested on Windows.

---

### Task 1: Core backend policy and recovery taxonomy

**Files:**
- Create: `src/Magic.Capture.Core/Capture/CaptureBackendPolicy.cs`
- Test: `tests/Magic.Capture.Core.Tests/CaptureBackendPolicyTests.cs`

**Interfaces:**
- Produces: `CaptureBackendKind`, `CaptureTargetKind`, `CaptureBackendPreference`, `CaptureBackendAvailability`, `CaptureBackendPolicy.BuildCandidates(...)`, `CaptureBackendFailureKind`, `CaptureBackendRecoveryPolicy`.

- [ ] Write tests for window/monitor/region/virtual ordering, cursor exclusion, forced preference, unavailable backends, and recovery classification.
- [ ] Run the focused xUnit test; if runtime is unavailable, run a source-contract RED check proving the production types are absent.
- [ ] Implement only the policy/taxonomy required by the tests.
- [ ] Re-run focused xUnit or source-contract GREEN check.

### Task 2: Backend boundary and GDI extraction

**Files:**
- Create: `src/Magic.Capture.App/Capture/ICaptureBackend.cs`
- Create: `src/Magic.Capture.App/Capture/GdiCaptureBackend.cs`
- Modify: `src/Magic.Capture.App/Capture/CaptureAttemptDiagnostics.cs`
- Modify: `src/Magic.Capture.App/Capture/ScreenCaptureService.cs`

**Interfaces:**
- Produces: `CaptureBackendRequest`, `CaptureBackendFrame`, `CaptureBackendProbe`, `CaptureBackendAttempt`, `ICaptureBackend`.
- Preserves: current GDI cursor rendering and retry semantics.

- [ ] Add source contract requiring GDI behind `ICaptureBackend` and no direct `CopyFromScreen` in `ScreenCaptureService`.
- [ ] Move bounded GDI implementation into `GdiCaptureBackend`.
- [ ] Expand diagnostics to hold an ordered attempt list.
- [ ] Keep `ScreenCaptureService` temporarily facade-compatible pending router task.

### Task 3: Shared D3D11 host

**Files:**
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Create: `src/Magic.Capture.App/Capture/Direct3D11DeviceHost.cs`

**Interfaces:**
- Produces: lazy hardware `ID3D11Device`, `ID3D11DeviceContext`, projected WinRT `IDirect3DDevice`, `Invalidate()`.

- [ ] Pin `Vortice.Direct3D11` and `Vortice.DXGI` to `3.8.3`; enable unsafe blocks only because the WGC activation-factory ABI helper requires a tiny isolated function-pointer bridge.
- [ ] Create BGRA-capable D3D11 hardware device and WinRT projection through `CreateDirect3D11DeviceFromDXGIDevice`.
- [ ] Make invalidation/disposal idempotent and bounded.

### Task 4: Windows Graphics Capture backend

**Files:**
- Create: `src/Magic.Capture.App/Capture/GraphicsCaptureItemInterop.cs`
- Create: `src/Magic.Capture.App/Capture/WindowsGraphicsCaptureBackend.cs`

**Interfaces:**
- Consumes: shared D3D host, `CaptureBackendRequest`.
- Produces: real WGC PNG frame for HWND/HMONITOR targets.

- [ ] Add source-contract RED checks for `IGraphicsCaptureItemInterop`, `CreateFreeThreaded`, `StartCapture`, `SoftwareBitmap.CreateCopyFromSurfaceAsync`, bounded timeout.
- [ ] Implement HWND/HMONITOR item creation using the WGC activation factory ABI.
- [ ] Implement first-frame capture, cancellation, cursor property gating, software-bitmap copy, PNG encoding, and dimension validation.
- [ ] Ensure all WinRT capture objects are disposed/closed even on timeout/error.

### Task 5: Desktop Duplication backend

**Files:**
- Create: `src/Magic.Capture.App/Capture/DesktopDuplicationCaptureBackend.cs`

**Interfaces:**
- Consumes: HMONITOR and physical requested bounds.
- Produces: BGRA8 PNG read back from `IDXGIOutputDuplication`.

- [ ] Add source-contract RED checks for adapter/output discovery, `DuplicateOutput`, `AcquireNextFrame`, `ReleaseFrame`, staging texture, map/unmap, access-lost recovery.
- [ ] Implement output lookup by HMONITOR and adapter-bound D3D11 device creation.
- [ ] Acquire/copy/map one frame with ≤1000 ms timeout and unconditional `ReleaseFrame`.
- [ ] Rotate according to `ModeRotation`, crop requested region, and validate output dimensions.
- [ ] Recreate once on access-lost/device-removed/reset; otherwise surface failure to router.

### Task 6: Router and capture entry-point integration

**Files:**
- Create: `src/Magic.Capture.App/Capture/CaptureBackendRouter.cs`
- Modify: `src/Magic.Capture.App/Capture/ScreenCaptureService.cs`
- Modify: `src/Magic.Capture.App/Capture/CaptureCoordinator.cs`
- Modify: `src/Magic.Capture.App/Capture/WindowCaptureService.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`

**Interfaces:**
- Produces: automatic backend selection for ordinary region/monitor/window/virtual captures.

- [ ] Wire Core policy to backend probes.
- [ ] Route window capture with HWND; monitor/region capture with HMONITOR when contained; virtual/cross-monitor capture with policy-safe GDI.
- [ ] Crop monitor-sized GPU frames for a region using physical monitor-local coordinates.
- [ ] Log only failover/total-failure diagnostics, not successful fast-path captures.
- [ ] Preserve public `ScreenCaptureService.Capture(...)` compatibility for existing callers such as scrolling capture.

### Task 7: Verification contracts, audit, release docs, and version

**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `release/feature-audit-660.json`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Create: `docs/RELEASE_NOTES_3.9.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`

**Interfaces:**
- Produces: 3.9 source-truth verifier and packaged-source release.

- [ ] Require all three real backend implementations and router integration in repository verifier.
- [ ] Promote audit #31/#32/#33/#34 only when contracts pass; keep #35–#38 and #39 unchanged.
- [ ] Bump version to 3.9.0 / 3.9.0.0.
- [ ] Add Windows gates for WGC occluded/minimized windows, DDA rotation, access-lost/mode switch, RDP/session change, GPU switching, fullscreen, cursor correctness, and GDI fallback.
- [ ] Run repository/structural/lexical/XML/audit/version gates on the final tree.
- [ ] Package deterministic source ZIP, re-extract it, run the same gates inside the ZIP, and produce SHA-256 sidecar.
