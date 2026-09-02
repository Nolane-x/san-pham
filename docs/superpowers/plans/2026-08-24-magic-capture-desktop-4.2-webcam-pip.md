# Magic Capture Desktop 4.2 Webcam/Picture-in-Picture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded local-native webcam capture and deterministic Picture-in-Picture composition to the existing MP4 recorder.

**Architecture:** Core owns option normalization, geometry, center-crop sampling and mask/blend behavior. App owns MediaCapture/MediaFrameReader, latest-frame buffering, camera enumeration and lifecycle; RecordingSessionService composes webcam frames into the existing screen frame before the existing encoder sees it.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Capture, Windows.Media.Capture.Frames, Windows.Graphics.Imaging, existing MediaStreamSource/MediaTranscoder recorder.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-4.2-webcam-pip-design.md`

## Global Constraints
- Windows minimum remains 10.0.19041.0.
- No cloud service or external FFmpeg dependency.
- Existing video-only and audio recorder paths remain backward compatible.
- Camera requested by the user is fail-closed.
- Latest-frame storage is bounded to one owned frame.
- Future recording manifest schemas remain read-only.
- Source release is not called Windows-build-passing without a real Windows/.NET build.

---

### Task 1: Webcam Core policy and compositor
**Files:**
- Modify: `src/Magic.Capture.Core/Recording/RecordingPolicy.cs`
- Create: `src/Magic.Capture.Core/Recording/RecordingWebcamPolicy.cs`
- Test: `tests/Magic.Capture.Core.Tests/RecordingWebcamPolicyTests.cs`

**Interfaces:**
- Produces `WebcamOverlayShape`, `RecordingWebcamPolicy`, `WebcamOverlayRect`, and normalized webcam fields on `RecordingOptions`.
- Produces `BgraWebcamCompositor.Composite(...)` used by the App frame compositor.

- [ ] Write tests for clamp/default/device-id, inside-frame geometry, aspect-fill crop, mirror, opacity, circle/rounded masking and saturating blend.
- [ ] Run source contract and confirm production symbols are absent.
- [ ] Implement policy/compositor with checked arithmetic and bounded pixels.
- [ ] Run lexical/structural source gates.

### Task 2: Camera catalog and latest-frame source
**Files:**
- Create: `src/Magic.Capture.App/Recording/CameraDeviceCatalog.cs`
- Create: `src/Magic.Capture.App/Recording/RecordingWebcamSource.cs`
- Test contract: `release/verify_complete_source.py`

**Interfaces:**
- `CameraDeviceCatalog.ListAsync(CancellationToken)` returns stable id/name records.
- `RecordingWebcamSource.StartAsync(deviceId, token)` initializes shared read-only video capture.
- `GetLatestFrame()` returns an owned immutable webcam frame snapshot.
- `Failure` exposes terminal source failure.

- [ ] Add release RED contract for MediaCapture/MediaFrameReader/deep-copy/latest-frame semantics.
- [ ] Implement enumeration and shared-read-only capture.
- [ ] Deep-copy BGRA8 in FrameArrived and dispose WinRT frame resources immediately.
- [ ] Add 5-second warm-up and bounded single-frame replacement.
- [ ] Re-run source gates.

### Task 3: Recording frame composition and lifecycle
**Files:**
- Modify: `src/Magic.Capture.App/Recording/RecordingFrameDecoder.cs`
- Create: `src/Magic.Capture.App/Recording/RecordingWebcamCompositor.cs`
- Modify: `src/Magic.Capture.App/Recording/RecordingSessionService.cs`
- Modify: `src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs`

**Interfaces:**
- Decoder exposes a BGRA byte path plus conversion to WinRT IBuffer.
- App compositor bridges owned webcam bytes to Core compositor.
- Session initializes webcam before active clock, composes each video frame, and disposes webcam in finally.

- [ ] Add source RED contract for session lifecycle and compositor wiring.
- [ ] Implement BGRA byte decode path while preserving old `DecodeBgra8Async`.
- [ ] Initialize/warm webcam before Recording state.
- [ ] Composite each frame only when enabled.
- [ ] Increment recording manifest schema to v3 while keeping v1/v2 compatibility.
- [ ] Re-run source gates.

### Task 4: Recorder UI and package privacy
**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`

**Interfaces:**
- UI builds normalized webcam options and refreshes cameras.
- Manifest declares `webcam` device capability.

- [ ] Add UI controls and intentionally observe missing-handler structural RED where applicable.
- [ ] Implement camera refresh, enable/disable logic, options mapping and active-recording locks.
- [ ] Add webcam capability after existing restricted capabilities in schema-valid location.
- [ ] Add user-facing camera status without exposing device identifiers.
- [ ] Re-run handler/XML/repository gates.

### Task 5: Audit, release contracts, docs and deterministic package
**Files:**
- Modify: `docs/FEATURE_AUDIT_660.md`
- Create: `docs/RELEASE_NOTES_4.2.0.md`
- Modify: Windows release checklist
- Modify: version files and `release/verify_complete_source.py`

**Interfaces:**
- Source-truth advances #54 Webcam capture, #55 Webcam PiP, #56 position/resize overlay to Done only if all end-to-end wiring exists.

- [ ] Add 4.2 verifier contracts for policy, source, compositor, UI, manifest and schema.
- [ ] Bump app/release/MSIX versions to 4.2.0 / 4.2.0.0.
- [ ] Update audit counts from exact ledger parsing.
- [ ] Run repository, structural, lexical, XML, audit and version gates.
- [ ] Create deterministic `Magic-Capture-Desktop-4.2.0-source.zip` and SHA sidecar.
- [ ] Extract the exact ZIP to a clean directory and rerun every gate from the bundle.
