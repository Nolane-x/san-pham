# Magic Capture Desktop 4.1.0 — Native Recording Audio

Magic Capture Desktop 4.1.0 extends the 4.0 visual recorder with a bounded, local Windows audio pipeline. System loopback and microphone capture remain separate adapters until they are normalized and mixed into the recorder's master 20 ms timeline.

## Added

- System-audio recording through WASAPI loopback.
- Microphone recording through WASAPI capture.
- Simultaneous system + microphone recording with independent 0–200% gains and saturating PCM16 mixing.
- Explicit active endpoint selection for render and capture devices, plus refreshable device lists.
- MSIX `microphone` device capability declaration for packaged microphone access; privacy denial remains a fail-closed runtime condition.
- Canonical 48 kHz, stereo, 16-bit PCM capture blocks with a fixed 20 ms cadence.
- Bounded two-second per-source PCM buffers; oldest buffered bytes are dropped under pressure and exposed through recorder diagnostics.
- Silence insertion at the master audio cadence when loopback produces no packet or a source temporarily has fewer bytes available.
- AAC output at configurable 96–320 kbps in the same MP4/H.264 container as video.
- Independent video-frame and audio-block sample indexes in the two-stream `MediaStreamSource`.
- Pause/resume clears source buffers and removes paused wall time from both encoded timelines.
- Live system/microphone peak status and dropped-byte diagnostics in the recording card.
- Recording journal schema 2 carrying normalized audio options while preserving legacy schema-1 readability and future-schema read-only protection.

## Reliability choices

- Requested audio is fail-closed: if the selected endpoint cannot start or stops unexpectedly, the session fails and the `.partial.mp4` plus recovery journal are preserved instead of silently producing a different recording.
- Audio adapters copy NAudio's zero-copy callback span immediately; no callback-owned memory escapes the callback lifetime.
- The mixer does not infer timeline gaps from WASAPI QPC positions because real shared-mode drivers may report those inconsistently. The master recording clock and fixed 20 ms sample cadence are authoritative.
- System/mic capture is lazy and exists only during an active recording; screenshot-only startup and idle behavior do not initialize WASAPI.

## Deliberately not claimed complete

- Audio-only recording.
- Webcam/PiP, GIF recording and post-recording timeline/audio editing.
- Automatic switching to a newly selected Windows default endpoint during an active session; 4.1 binds the endpoint selected at start.
- Guaranteed long-run A/V drift bounds or codec/device behavior until Windows hardware/runtime validation is executed.
- Hardware AAC/H.264 acceleration guarantees; the existing MediaTranscoder hardware request remains a runtime-dependent Partial capability.

## Source-truth audit

- Done: **352**
- Partial: **63**
- Foundation: **139**
- Missing: **84**
- ReleaseTest: **22**
- Total: **660**

## Verification boundary

Repository, structural, lexical, audit, XML and deterministic source-bundle verification can run in the current environment. This environment does not provide the .NET/WinUI/MSIX Windows build toolchain or Windows audio/media runtime, so xUnit execution, compilation, WASAPI endpoint behavior, AAC/H.264 transcode, long-run A/V drift and x64/ARM64 runtime validation remain Windows release gates.
