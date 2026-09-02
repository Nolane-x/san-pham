# Magic Capture Desktop 3.9.0 — Capture Backend Architecture

Magic Capture Desktop 3.9.0 replaces the single-path screenshot core with a target-aware Windows capture backend router. The release keeps GDI as the universal correctness fallback while adding GPU-backed Windows Graphics Capture and DXGI Desktop Duplication paths behind one bounded policy and one diagnostics model.

## Capture backends

### Windows Graphics Capture (WGC)

- Creates `GraphicsCaptureItem` directly from HWND/HMONITOR through `IGraphicsCaptureItemInterop`.
- Uses a shared D3D11 BGRA device and `Direct3D11CaptureFramePool.CreateFreeThreaded` so first-frame capture is not tied to a UI dispatcher.
- Bounds first-frame wait to 1.5 seconds.
- Honors the requested cursor contract when the platform exposes cursor-capture control; otherwise fails closed instead of silently returning the wrong cursor state.
- Converts the captured Direct3D surface to a bounded PNG payload and validates physical-pixel dimensions before accepting the frame.

### DXGI Desktop Duplication (DDA)

- Matches a concrete `HMONITOR` to its DXGI output and uses `DuplicateOutput` on an adapter-bound D3D11 device.
- Copies the acquired desktop frame through a CPU-readable staging texture with bounded dimension validation.
- Applies output rotation before handing the frame to the router.
- Classifies `DXGI_ERROR_ACCESS_LOST`, device-removed and device-reset failures and allows at most one full duplication-interface rebuild.
- Preserves the exact failure taxonomy after the rebuild budget is exhausted so fallback diagnostics remain truthful.
- Fails closed on cursor exclusion: a DDA frame is accepted for cursor-off capture only when frame metadata proves that the pointer is a separate visible overlay. Embedded/ambiguous pointer state falls through to GDI.
- Pointer-shape composition for cursor-on DDA capture is intentionally not implemented in 3.9.0.

### Bounded GDI fallback

- `CopyFromScreen` now lives behind the same `ICaptureBackend` contract as GPU backends.
- Retains the existing three-attempt bounded transient retry policy.
- Continues to support explicit cursor composition.
- Validates encoded PNG dimensions and reports physical-pixel bounds on terminal failure.

## Automatic routing

The Core policy builds candidates from target geometry, cursor requirements, backend availability and an optional backend preference without allowing that preference to bypass correctness rules.

Default routing in 3.9.0:

- Window: `WGC → GDI`.
- Monitor or single-monitor region, cursor off: `WGC → DDA → GDI`.
- Monitor or single-monitor region, cursor on: `WGC → GDI`.
- Cross-monitor region and virtual desktop: `GDI` only.

Cancellation is terminal and never triggers a fallback capture. Non-cancellation backend failures are recorded as bounded attempt diagnostics before the router tries the next applicable backend.

## Safety and compatibility boundaries

3.9.0 deliberately does **not** claim the following as completed:

- HDR screenshot preservation or HDR-to-SDR tone mapping.
- Wide-color / ICC-aware GPU capture.
- 10-bit desktop processing.
- Cross-monitor GPU composition.
- Desktop Duplication pointer-shape composition.
- Runtime certification for protected, exclusive-fullscreen or unusual D3D applications.
- Recording/video/GIF capture; this remains a separate subsystem.

The App project now references `Vortice.Direct3D11` and `Vortice.DXGI` 3.8.3 for D3D11/DXGI bindings rather than maintaining handwritten COM vtables for the Desktop Duplication surface.

## Source verification status

The source-release gate validates repository contracts, C# lexical integrity, project/XAML/XML structure, the exact 660-feature ledger, backend dependency pins, backend policy wiring, DDA cursor fail-closed logic and release metadata.

The current build environment does not provide the .NET SDK, MSBuild, Visual Studio or Windows SDK. Therefore xUnit execution, WinUI compilation, MSIX packaging and Windows runtime/hardware validation remain mandatory external release gates and are not represented as completed by this source release.
