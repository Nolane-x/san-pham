# Magic Capture Desktop 4.0 Visual Recording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local MP4/H.264 visual recorder for region, window, monitor, and virtual desktop targets with pause/resume, bounded options, and crash-aware session journaling.

**Architecture:** Core owns deterministic recording policy and state. App reuses the 3.9 screenshot backend router to obtain frames, exposes BGRA8 frames through MediaStreamSource, encodes with MediaTranscoder, and atomically finalizes a `.partial.mp4` file. A single session service owns lifecycle and UI events.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Core, Windows.Media.Transcoding, Windows.Media.MediaProperties, Windows.Graphics.Imaging, existing Magic Capture 3.9 capture router, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-4.0-visual-recording-design.md`

## Global Constraints

- One active recording maximum.
- Visual-only MP4/H.264 in 4.0; no audio/webcam/GIF/editor claims.
- FPS 5–60; bitrate 1–50 Mbps; scale 25–100%; countdown 0–10 s; max duration 1–240 min.
- Output dimensions must be even and at least 2×2.
- Pause time is excluded from the encoded timeline.
- Capture failure is terminal; never reuse a stale frame.
- Write to `.partial.mp4`; only successful completion can move to final `.mp4`.
- Future journal schema is read-only.
- Windows runtime claims remain external until tested on Windows.

---

### Task 1: Core recording policy and state machine

**Files:**
- Create: `src/Magic.Capture.Core/Recording/RecordingPolicy.cs`
- Test: `tests/Magic.Capture.Core.Tests/RecordingPolicyTests.cs`

**Interfaces:** Produces `RecordingOptions`, `RecordingTargetKind`, `RecordingSessionState`, `RecordingRules`, `RecordingCadence`, `RecordingStopPolicy`, `RecordingStateMachine`.

- [ ] Write tests for normalization, even dimensions, frame timestamps, stop duration, and legal/illegal state transitions.
- [ ] Run a source-contract RED check when xUnit runtime is unavailable.
- [ ] Implement minimal policy/state production types.
- [ ] Re-run source-contract GREEN and lexical verifier.

### Task 2: Crash-safe recording journal

**Files:**
- Create: `src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs`
- Test: `tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs`
- Modify: `src/Magic.Capture.App/Persistence/AppPaths.cs`

**Interfaces:** Produces `RecordingSessionManifest`, atomic save/delete/load-unfinished, future-schema read-only behavior.

- [ ] Add manifest policy tests first.
- [ ] Add recording paths and atomic journal store.
- [ ] Harden size/path/schema validation and backup recovery.

### Task 3: Recording target frame provider

**Files:**
- Create: `src/Magic.Capture.App/Recording/RecordingTarget.cs`
- Create: `src/Magic.Capture.App/Recording/RecordingFrameProvider.cs`
- Create: `src/Magic.Capture.App/Recording/RecordingFrameDecoder.cs`
- Modify: `src/Magic.Capture.App/Capture/WindowCaptureService.cs`

**Interfaces:** Produces target descriptors and bounded BGRA8 frame buffers at normalized dimensions.

- [ ] Add source-contract RED checks for all four target kinds and transformed BGRA8 decode.
- [ ] Implement dynamic window/monitor/region/virtual capture through existing router.
- [ ] Decode/scale PNG with BitmapDecoder transform and validate exact buffer length.

### Task 4: MP4 MediaStreamSource encoder

**Files:**
- Create: `src/Magic.Capture.App/Recording/Mp4RecordingEncoder.cs`

**Interfaces:** Consumes async BGRA frame callback and recording lifecycle signals; produces temporary MP4/H.264 output.

- [ ] Add verifier contract for `MediaStreamSource`, `SampleRequested`, deferral, null EOS, `PrepareMediaStreamSourceTranscodeAsync`, `CreateMp4`, H.264 profile, hardware acceleration, and audio removal.
- [ ] Implement monotonic paced sample production and pause waiting.
- [ ] Implement transcode preparation/failure reporting and cancellation-safe cleanup.

### Task 5: Recording session orchestrator

**Files:**
- Create: `src/Magic.Capture.App/Recording/RecordingSessionService.cs`
- Modify: `src/Magic.Capture.App/ApplicationServices.cs`
- Modify: `src/Magic.Capture.App/App.xaml.cs`

**Interfaces:** Produces Start/Pause/Resume/Stop, progress/state events, last-region target, active session snapshot.

- [ ] Enforce single-session gate and countdown.
- [ ] Persist journal through preparing/recording/paused/finalizing/failure.
- [ ] Atomically finalize temporary output to requested final path.
- [ ] Preserve last successful recording-region descriptor for repeat.

### Task 6: MainWindow recording UI

**Files:**
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:** Exposes target selection, settings, Start/Pause/Resume/Stop, timer/status, unfinished-session notification.

- [ ] Add Recording card to Home.
- [ ] Add target pickers for Region / Foreground Window / Active Monitor / Virtual Desktop and Repeat Last Region.
- [ ] Apply `WDA_EXCLUDEFROMCAPTURE` before countdown/recording and restore `WDA_NONE` after finalization/failure; fail closed if exclusion cannot be applied.
- [ ] Keep elapsed/status updates on UI dispatcher.

### Task 7: Audit, verifier, version, release

**Files:**
- Modify: `scripts/verify-repo.py`
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `release/feature-audit-660.json`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Create: `docs/RELEASE_NOTES_4.0.0.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`

**Interfaces:** Produces truthful 4.0 source release.

- [ ] Promote only source-backed visual recording capabilities; do not promote audio/webcam/GIF/editing.
- [ ] Bump 4.0.0 / 4.0.0.0.
- [ ] Add Windows codec/hardware/long-run/pause/crash/session-change gates.
- [ ] Run repository/structural/lexical/XML/audit/version gates.
- [ ] Package deterministic ZIP, re-extract, run same gates, and emit SHA-256 sidecar.
