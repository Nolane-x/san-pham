# Magic Capture Desktop 4.0 Visual Recording Design

## Scope

Version 4.0 adds a local, crash-aware visual screen recorder without external services or an FFmpeg dependency. It records region, window, monitor, and virtual-desktop targets to MP4/H.264, reusing the 3.9 capture router so screenshot and recording obey the same physical-pixel, cursor, mixed-DPI, and backend-fallback rules.

Audio, webcam, HEVC/AV1/VP9, GIF/APNG/WebP, drawing overlays, and timeline editing are explicitly deferred. They must build on this recorder rather than being mixed into its first release.

## Architecture

1. `Magic.Capture.Core.Recording` owns pure policy: option validation, frame cadence, scale normalization, state transitions, stop-after-duration, and session metadata rules.
2. `RecordingTarget` in the App layer carries the dynamic Windows target identity (bounds, monitor handle, HWND) and captures one frame through existing capture services.
3. `RecordingFrameDecoder` converts each PNG capture to bounded BGRA8 and applies requested output scaling during decode.
4. `Mp4RecordingEncoder` exposes the frames through `MediaStreamSource` and encodes them with `MediaTranscoder` + H.264 MP4. Frame requests are paced against a monotonic recording clock. A null sample ends the stream.
5. `RecordingSessionService` owns countdown, start/pause/resume/stop, a single active session, stop-after-duration, progress events, and crash journal lifecycle.
6. `RecordingRecoveryStore` persists an atomic JSON session journal beside a temporary output. On next launch it reports unfinished recordings without automatically claiming a partial MP4 is playable.
7. MainWindow exposes a compact Recording card with target, FPS, bitrate, scale, cursor, countdown, max minutes, Start/Pause/Resume/Stop, elapsed time, and last-output status. The top-level control window uses `WDA_EXCLUDEFROMCAPTURE` for the active recording lifetime; start fails closed if Windows cannot apply the exclusion. Region selection temporarily hides the main window only while the selection overlay is active.

## Invariants

- Exactly one recording session may be active.
- FPS range: 5–60. Default 30.
- Bitrate range: 1–50 Mbps. Default 8 Mbps.
- Scale: 25–100%, dimensions rounded down to an even number, minimum 2×2.
- Countdown: 0–10 seconds.
- Optional maximum duration: 1–240 minutes.
- Frame sample timestamps are monotonic and exclude paused wall time.
- Pause never writes duplicate timeline time; resume continues from the next sample index.
- Capture failure is terminal for the session; it does not silently encode a stale previous frame.
- Output is written to a temporary `.partial.mp4` path and moved to the requested final path only after successful transcode completion.
- Journal writes use replace/backup semantics; a future journal schema is read-only and never overwritten by an older app.
- No credentials, AI state, or network access are involved.
- Windows build/runtime and codec availability remain Windows release gates.

## Media pipeline

`VideoEncodingProperties.CreateUncompressed(BGRA8, width, height)` describes source samples. `MediaStreamSource.SampleRequested` obtains a deferral, waits until the next frame is due, captures/decode-scales exactly one frame, creates `MediaStreamSample.CreateFromBuffer`, sets duration and timestamp, and completes the request. When Stop is requested or max duration is reached, the request receives no sample, which is end-of-stream.

`MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto)` supplies H.264 MP4 output. The profile is normalized to requested dimensions, FPS, and bitrate; audio is removed for 4.0. `MediaTranscoder.HardwareAccelerationEnabled = true` requests native hardware acceleration while leaving runtime fallback to Windows.

## Recovery

Journal states: `Preparing`, `Recording`, `Paused`, `Finalizing`, `Completed`, `Failed`. The journal records schema version, session ID, requested final path, temporary path, target summary, options, started/updated UTC, frame count, and active elapsed time. Startup recovery only surfaces unfinished entries and their partial file; it never renames a partial file to `.mp4` automatically.

## Source-truth audit targets

4.0 may promote only capabilities demonstrated by source contracts: record region/window/monitor/virtual desktop, pause/resume, countdown, elapsed timer, hiding controls, cursor toggle, FPS, bitrate, resolution scale, H.264 MP4, hardware-acceleration request, stop after N minutes, repeat last recording region, and unfinished-recording detection. Multiple-monitor recording is covered only by virtual desktop; the separate multi-monitor-selection feature remains Missing until selectable subsets exist. Audio/webcam/GIF/editor items remain unchanged.
