# Magic Capture Desktop 4.1 Recording Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded local system-audio + microphone capture, mixing, AAC muxing, device selection, and A/V lifecycle integration to the 4.0 recorder.

**Architecture:** NAudio 3 WASAPI adapters capture canonical PCM16/48k/stereo into bounded buffers. Core handles deterministic audio policy/mixing; `Mp4RecordingEncoder` exposes two MediaStreamSource descriptors when audio is enabled; `RecordingSessionService` remains lifecycle authority.

**Tech Stack:** .NET 10, WinUI 3, Windows.Media.Core, Windows.Media.Transcoding, NAudio.Wasapi 3.0.1, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-4.1-recording-audio-design.md`

## Global Constraints
- Windows minimum remains 10.0.19041.0.
- Audio canonical format is PCM16, 48,000 Hz, stereo.
- Audio block duration is 20 ms.
- Per-source buffered audio is bounded to two seconds.
- No FFmpeg or cloud service.
- No silent downgrade when a requested audio source fails.
- Video-only 4.0 behavior remains valid when both audio sources are disabled.

---

### Task 1: Core audio policy and mixing
**Files:** Create `src/Magic.Capture.Core/Recording/RecordingAudioPolicy.cs`; modify `RecordingPolicy.cs`; create `tests/Magic.Capture.Core.Tests/RecordingAudioPolicyTests.cs`.
**Produces:** normalized audio options, deterministic cadence, PCM mixer, levels, gap math.
- [ ] Write tests for bitrate/gain clamping, 20 ms cadence, saturating dual-source mix, single-source gain, silence, peak/RMS, and QPC gap conversion.
- [ ] Verify source-contract RED because `RecordingAudioPolicy` does not exist.
- [ ] Implement the minimal Core policy and mixer.
- [ ] Run lexical/structural verification.

### Task 2: WASAPI device catalog and bounded capture buffers
**Files:** modify App csproj; create `Recording/AudioDeviceCatalog.cs`, `Recording/BoundedPcmBuffer.cs`, `Recording/WasapiRecordingAudioSource.cs`, `Recording/RecordingAudioPipeline.cs`.
**Produces:** device enumeration, default/explicit source creation, bounded PCM capture, mixed 20 ms samples.
- [ ] Add source-contract checks before production files.
- [ ] Add `NAudio.Wasapi` 3.0.1.
- [ ] Implement render/capture endpoint catalog and source wrappers.
- [ ] Implement two-second bounded buffers with oldest-drop policy and silence-fill reads.
- [ ] Implement the pipeline with pause/resume buffer flushing, QPC diagnostics, levels, and fail-closed requested-source errors.
- [ ] Run repository/lexical verification.

### Task 3: Two-stream MediaStreamSource encoder
**Files:** modify `Recording/Mp4RecordingEncoder.cs`.
**Consumes:** optional `Func<long,CancellationToken,Task<IBuffer?>>` audio factory.
**Produces:** H.264 + AAC MP4 while preserving video-only behavior.
- [ ] Add verifier contract for `AudioStreamDescriptor`, two-descriptor `MediaStreamSource`, request routing, PCM input, AAC profile, separate gates.
- [ ] Extend encoder API with optional audio factory and AAC bitrate.
- [ ] Ensure video/audio EOS are independent and exceptions notify the source without swallowing failures.
- [ ] Run lexical/structural verification.

### Task 4: Recording lifecycle integration and recovery schema v2
**Files:** modify `RecordingSessionService.cs`, `RecordingRecoveryStore.cs`, `RecordingPolicy.cs`.
**Produces:** audio starts/stops with recording, shared pause gate, journal persistence, schema-v2 compatibility.
- [ ] Add manifest/policy tests for schema v2 and backward readability.
- [ ] Start audio before transcoding, route audio samples through the session pause/stop policy, and stop/dispose audio on every exit path.
- [ ] Clear audio buffers on pause/resume and publish audio level/source status.
- [ ] Advance recording journal schema to 2 while keeping future-schema read-only behavior.
- [ ] Run verification.

### Task 5: Recording UI and device selection
**Files:** modify `MainWindow.xaml`, `MainWindow.xaml.cs`, `ApplicationServices.cs`.
**Produces:** independent system/mic selectors, gains, AAC bitrate, refresh, level telemetry.
- [ ] Add XAML first so handler verifier is RED.
- [ ] Wire catalog refresh and stable endpoint IDs.
- [ ] Extend `ReadRecordingOptions` and active-control state.
- [ ] Surface requested-source startup/runtime failures clearly.
- [ ] Run structural/repository verification.

### Task 6: Audit, Windows gates, version and release
**Files:** update feature audit JSON/MD, README/version/MSIX/release notes/checklist/verifier.
**Produces:** 4.1.0 source-truth release.
- [ ] Move only implemented audio audit items to Done.
- [ ] Add Windows runtime matrix for system/mic/both/silence/device-loss/pause-drift.
- [ ] Bump 4.1.0 / 4.1.0.0.
- [ ] Run all static gates.
- [ ] Create deterministic source ZIP and SHA-256.
- [ ] Extract the ZIP cleanly and rerun all gates on the packaged source.
