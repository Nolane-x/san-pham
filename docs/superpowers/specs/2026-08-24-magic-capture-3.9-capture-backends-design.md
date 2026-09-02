# Magic Capture Desktop 3.9 — Multi-Backend Capture Engine Design

## Goal

Replace the single-backend GDI capture path with a bounded, diagnosable backend router that can use Windows Graphics Capture (WGC), Desktop Duplication (DXGI/D3D11), and GDI in a deterministic fallback chain while preserving Magic Capture's physical-pixel, local-first invariants.

## Scope

This wave implements five connected capabilities:

1. **Backend policy in Core** — deterministic candidate ordering by capture target, cursor requirement, backend availability, and optional forced preference.
2. **Windows Graphics Capture backend** — programmatic HWND/HMONITOR capture through `IGraphicsCaptureItemInterop`, a free-threaded WGC frame pool, a shared D3D11 device, bounded first-frame timeout, and PNG output via `SoftwareBitmap`.
3. **Desktop Duplication backend** — single-output DXGI duplication with adapter/output discovery, staging-texture readback, rotation correction, bounded frame acquisition, and explicit recreation on access-lost/device-loss conditions.
4. **GDI fallback backend** — preserve the 3.8 bounded `CopyFromScreen` retry behavior as the terminal compatibility backend.
5. **Router diagnostics and health** — every capture records attempted backends, skip/failure reasons, chosen backend, recovery count, and capability probe state without silently swallowing backend failures.

Recording/video, HDR/10-bit output, wide-color export, and cross-output synchronized Desktop Duplication remain separate work. This release may capture SDR PNG from modern GPU paths, but it must not promote HDR audit items.

## External API grounding

- WGC Win32 interop can create capture items for HWND/HMONITOR through `IGraphicsCaptureItemInterop` on Windows 10 1903+.
- `Direct3D11CaptureFramePool.CreateFreeThreaded` removes the dependency on a UI `DispatcherQueue` and raises `FrameArrived` on an internal worker thread.
- Desktop Duplication is created per adapter output. `DXGI_ERROR_ACCESS_LOST` invalidates the duplication interface and requires recreation; desktop/mode switches can trigger this state.
- Desktop Duplication surfaces are BGRA8 SDR and must be rotated according to the output rotation before being interpreted in physical desktop coordinates.

## Architecture

### 1. Core backend policy

Add `CaptureBackendKind`, `CaptureTargetKind`, `CaptureBackendAvailability`, `CaptureBackendPreference`, `CaptureBackendDecision`, and `CaptureBackendPolicy` under `Magic.Capture.Core.Capture`.

Default ordering:

- **Window:** WGC → GDI.
- **Monitor:** WGC → Desktop Duplication → GDI.
- **Single-monitor Region:** WGC → Desktop Duplication → GDI.
- **Virtual Desktop / cross-monitor Region:** GDI in 3.9 because WGC has no virtual-desktop item and Desktop Duplication would require multi-output timestamp synchronization.

If cursor capture is requested, Desktop Duplication is skipped in 3.9 because pointer composition is not implemented yet; WGC or GDI must be used so the caller never gets a silently missing cursor.

A forced backend preference is honored only when the backend is applicable and available. `Auto` remains the product default.

### 2. Backend request/result boundary

App layer introduces `ICaptureBackend` with:

- `CaptureBackendKind Kind`
- `CaptureBackendProbe Probe()`
- `Task<CaptureBackendFrame> CaptureAsync(CaptureBackendRequest request, CancellationToken cancellationToken)`

`CaptureBackendRequest` carries physical desktop bounds, target kind, HWND/HMONITOR when applicable, cursor requirement, and the source/output metadata needed to create a `CaptureAsset` only after a backend succeeds.

`CaptureBackendFrame` contains PNG bytes plus the physical bounds that the frame represents. The router verifies dimensions and crops a monitor-sized GPU frame to a requested single-monitor region before creating the final asset.

### 3. Shared D3D11 device

Use Vortice.Direct3D11/Vortice.DXGI 3.8.3 to avoid hand-maintaining large COM vtables. `Direct3D11DeviceHost` creates a BGRA-capable hardware D3D11 device and exposes:

- the Vortice `ID3D11Device`/context for Desktop Duplication;
- a projected WinRT `Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice` created through `CreateDirect3D11DeviceFromDXGIDevice` for WGC.

The host is lazy. Device-removal/reset invalidates the host; the next GPU capture recreates it. The router never falls back to WARP for screen capture because the screen source is tied to real display adapters.

### 4. WGC backend

`WindowsGraphicsCaptureBackend`:

- probes `GraphicsCaptureSession.IsSupported()` plus minimum OS interop availability;
- creates `GraphicsCaptureItem` for HWND/HMONITOR with the activation-factory `IGraphicsCaptureItemInterop` ABI;
- creates a free-threaded BGRA8 frame pool with two buffers;
- starts a capture session and waits for the first frame with a hard timeout;
- enables cursor capture when the property is available and the request requires it;
- deep-copies the frame surface to `SoftwareBitmap`, converts to BGRA8/Premultiplied if needed, and encodes PNG through `BitmapEncoder`;
- validates non-zero content size and encoded dimensions;
- closes session/frame/frame-pool deterministically.

A timeout, closed item, access denial, device removal, or unexpected dimensions is a backend failure, not a process failure. The router records it and tries the next candidate.

### 5. Desktop Duplication backend

`DesktopDuplicationCaptureBackend`:

- locates the DXGI adapter/output whose `OutputDescription.Monitor` equals the requested HMONITOR;
- creates an adapter-bound D3D11 device;
- creates `IDXGIOutputDuplication` and waits for a frame with a bounded timeout;
- copies the desktop texture into a CPU-readable staging texture;
- maps it and copies BGRA rows into a `Bitmap`;
- applies `ModeRotation.Rotate90/180/270` correction;
- validates dimensions against physical monitor bounds after rotation;
- crops to the requested region if needed;
- always releases the acquired frame.

`DXGI_ERROR_ACCESS_LOST`, device removed/reset, desktop switch, or mode change invalidate the backend session. 3.9 retries by rebuilding the duplication objects once inside the backend before returning failure to the router. `WAIT_TIMEOUT` is bounded and never loops indefinitely.

Desktop Duplication does not compose mouse pointer shape in 3.9; the Core policy therefore excludes it when `IncludeCursor=true`.

### 6. GDI backend

Move the existing bounded `CopyFromScreen` implementation behind `GdiCaptureBackend`. Preserve:

- maximum 3 attempts;
- retry only `ExternalException`/`Win32Exception`;
- 40 ms retry delay;
- cursor drawing;
- PNG dimension validation.

GDI remains the compatibility endpoint and the only 3.9 backend for virtual-desktop and cross-monitor arbitrary regions.

### 7. Router and integration

`CaptureBackendRouter` owns the backend instances and executes `CaptureBackendPolicy`. It never retries a backend outside that backend's explicit recovery budget. It returns `CaptureWithDiagnosticsResult` containing:

- chosen backend;
- ordered attempts;
- per-attempt duration;
- failure/skip reason;
- whether recovery/recreation occurred;
- physical bounds.

`ScreenCaptureService` becomes a facade over the router. `CaptureCoordinator` passes HMONITOR for monitor/overlay captures and resolves a containing monitor for a region. `WindowCaptureService` passes HWND directly so WGC can capture occluded windows when supported.

### 8. Diagnostics / user-visible behavior

Normal capture remains silent and fast. Failover is written to the existing local log only when the preferred backend fails or the router falls back to GDI. Error messages on total failure list the candidate chain without including sensitive content.

No telemetry or cloud reporting is added.

### 9. Testing and release truth

Core xUnit tests cover:

- candidate ordering;
- cursor exclusion of Desktop Duplication;
- cross-monitor/virtual-desktop GDI-only behavior;
- forced preference behavior;
- capability-unavailable filtering;
- recovery classification (`AccessLost`, `DeviceRemoved`, timeout, permanent failure).

App source contracts verify:

- WGC uses `CreateFreeThreaded` and `IGraphicsCaptureItemInterop`;
- Desktop Duplication uses `DuplicateOutput`, `AcquireNextFrame`, `ReleaseFrame`, staging readback, and access-lost recovery;
- GDI is an `ICaptureBackend`, not a parallel un-routed path;
- `ScreenCaptureService` routes through `CaptureBackendRouter`;
- Vortice package versions are pinned to 3.8.3.

This Linux environment has no .NET/Windows SDK runtime. Static/source verification can establish architecture contracts only. xUnit, WinUI compilation, MSIX packaging, WGC runtime, DXGI runtime, device-loss, fullscreen, RDP, secure-desktop, and mixed-GPU behavior remain Windows release gates.

## Audit truth

Promote only when source implementation exists:

- #31 Windows Graphics Capture GPU path → Done after the real WGC frame path exists.
- #32 Desktop Duplication fallback → Done after real `DuplicateOutput/AcquireNextFrame` readback exists.
- #33 Legacy GDI fallback when needed → Done once GDI is the terminal backend in a real router chain.
- #34 Automatic engine selection → Done once Core policy + App router are wired into all ordinary screen/window/monitor capture entry points.

Keep #35–#38 HDR/wide-color/10-bit as Missing and #39 high-refresh monitor testing as ReleaseTest.

## Safety limits

- First WGC frame timeout: 1500 ms.
- Desktop Duplication `AcquireNextFrame`: maximum 1000 ms per attempt.
- Desktop Duplication recreation budget: 1 rebuild per capture call.
- GDI retry budget: existing maximum 3 attempts.
- GPU output dimensions and encoded bytes remain subject to `ImageWorkloadLimits`.
- No backend loops indefinitely or retries after cancellation.
