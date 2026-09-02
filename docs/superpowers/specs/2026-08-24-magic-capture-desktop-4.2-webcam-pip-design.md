# Magic Capture Desktop 4.2 Webcam/Picture-in-Picture Design

## Goal
Add a local-native webcam capture and Picture-in-Picture subsystem to the existing visual/audio recorder without creating a second recording pipeline or weakening failure/recovery semantics.

## Scope
4.2 implements camera discovery, explicit camera selection, webcam frame capture, latest-frame buffering, deterministic BGRA composition into the existing recording frame, overlay positioning/resizing, rectangle/rounded/circle masks, mirroring, opacity, UI controls, manifest persistence, camera privacy capability, diagnostics, and release contracts.

Out of scope for 4.2: AI background removal, person segmentation, chroma key, virtual backgrounds, webcam-only recording, camera audio, camera control mutation (focus/exposure/white balance), and GPU shader composition. These remain later waves so the camera path can be validated first.

## Architecture

### Core policy
`Magic.Capture.Core.Recording` owns webcam option normalization and deterministic overlay geometry/mask/blend rules. It never references WinRT.

`RecordingOptions` gains backward-compatible optional webcam fields. Existing serialized manifests continue to deserialize because every new positional record parameter has a default.

### Camera adapter
`CameraDeviceCatalog` enumerates `DeviceClass.VideoCapture` devices.

`RecordingWebcamSource` owns one `MediaCapture` and one `MediaFrameReader`. It initializes with the selected `VideoDeviceId`, `StreamingCaptureMode.Video`, `MediaCaptureMemoryPreference.Cpu`, and `MediaCaptureSharingMode.SharedReadOnly`.

It selects a color frame source, requests BGRA8 frames, starts the reader, and uses `FrameArrived -> TryAcquireLatestFrame`. The handler immediately deep-copies the `SoftwareBitmap` into owned BGRA bytes and disposes frame references/temporary bitmaps. Only one latest owned frame is retained; replacement is atomic and bounded.

Camera access is fail-closed when requested. If initialization fails, permission is denied, the camera disappears, or no usable color source exists, the recording does not silently continue without webcam.

### Frame composition
Screen capture remains authoritative. `RecordingFrameProvider` is unchanged.

`RecordingSessionService` initializes webcam before the active clock starts. For each video sample:
1. capture the normal recording target through the 3.9 capture router;
2. decode/scale it to BGRA8;
3. obtain the latest webcam BGRA frame;
4. composite the webcam frame with Core geometry/blend rules;
5. pass the final BGRA8 buffer to `Mp4RecordingEncoder`.

If webcam is enabled but no first frame arrives within a bounded warm-up timeout, Start fails. After warm-up, short camera gaps reuse the last owned frame; a camera-source terminal failure fails the session rather than silently dropping PiP.

### Overlay policy
Overlay coordinates are output-frame percentages so they remain stable across recording scale settings.

Supported properties:
- X/Y position: 0..100 percent, clamped so the overlay remains inside the output frame.
- Width: 10..50 percent of output width.
- Shape: rectangle, rounded, circle.
- Mirror horizontally.
- Opacity: 20..100 percent.
- Border: 0..12 pixels, white with matching opacity.

The compositor preserves camera aspect ratio using center-crop fill. It performs bounded bilinear sampling and premultiplied BGRA alpha blending. Circle/rounded masks are applied deterministically in Core.

### Pause/resume and recovery
Webcam capture remains alive while recording is paused so resume does not require camera reinitialization; no webcam samples are encoded while the recording clock is paused because video sampling itself is paused.

The recording manifest schema increments to v3. Schema v1/v2 remain readable/writable under backward-compatibility rules; future schemas remain read-only. Webcam device ID and overlay settings are persisted because they are not secrets.

### UI
The recorder card gains:
- Webcam toggle
- camera ComboBox + Refresh
- position preset and X/Y controls
- size slider/NumberBox
- shape selector
- mirror toggle
- opacity control
- border width control
- live camera status

Camera controls are disabled for an active recording.

### Privacy and permissions
The packaged app declares `<DeviceCapability Name="webcam" />` in addition to existing microphone capability. Permission denial must fail cleanly and must not create a promoted final MP4.

## Boundedness and failure rules
- Latest camera buffer only: no unbounded frame queue.
- Maximum webcam frame pixels are checked with existing image workload limits.
- Warm-up timeout is 5 seconds.
- Camera device IDs are capped at 1024 characters.
- No stale frame may survive a terminal source failure.
- No final-path promotion occurs after webcam failure.
- Cleanup errors are logged and do not replace the primary failure.

## Verification
Source-level verification must assert Core policy/tests, MediaCapture/MediaFrameReader ownership, bounded latest-frame behavior, compositor use, UI wiring, webcam manifest capability, schema v3, and the 660-feature audit.

Windows release testing must cover permission denial, no-camera machines, USB unplug, device busy/shared camera, 720p/1080p cameras, mirror/masks/opacity, 5/30/60 FPS, pause/resume, 2-hour A/V+webcam drift, x64/ARM64, and control-window exclusion.
