# Magic Capture Desktop 4.1 Recording Audio Design

## Goal
Add local system-audio and microphone recording to the existing 4.0 visual recorder without creating a second video pipeline or weakening crash-safe `.partial.mp4` semantics.

## Scope
4.1 supports system loopback audio, microphone audio, or both at once; explicit device selection; independent source gains; AAC-in-MP4 output; bounded buffering; silence insertion; pause/resume semantics; basic level telemetry; and journaled audio configuration. Audio-only recording, per-process loopback, webcam/PiP, audio editing, and unfinished-MP4 reconstruction remain outside this wave.

## Architecture
The recorder keeps `RecordingSessionService` as lifecycle authority. Windows audio capture is isolated behind `RecordingAudioPipeline`, which owns two optional WASAPI sources implemented with NAudio 3 `WasapiRecorder`. Both sources request canonical PCM 48 kHz, 16-bit stereo in shared mode. Core owns option normalization, 20 ms sample cadence, saturating PCM mixing, silence/gap math, and peak/RMS calculations.

`Mp4RecordingEncoder` becomes a two-stream `MediaStreamSource` when audio is enabled. Video remains BGRA8/H.264. Audio input is uncompressed PCM and the output profile uses AAC. Audio and video have separate sample gates and timestamps, both starting at zero. Pauses emit neither video nor audio samples; buffered paused audio is discarded on pause/resume so paused wall time never appears in the media timeline.

## Safety and boundedness
- Audio sample rate is fixed at 48,000 Hz, stereo, PCM16.
- Audio blocks are 20 ms (960 frames / 3,840 bytes).
- Each capture source buffer is bounded to 2 seconds. Oldest buffered audio is dropped rather than permitting unbounded growth.
- Missing loopback packets become silence; stale audio is never replayed.
- Capture callbacks copy their span immediately because NAudio's zero-copy buffer lifetime ends with the callback.
- Device loss or source failure fails the recording rather than silently removing a requested track.
- `SystemAudio=false` and `Microphone=false` preserves the 4.0 video-only path.
- Future journal schema remains read-only.

## Device model
`AudioDeviceCatalog` enumerates active WASAPI render and capture endpoints by stable endpoint ID and friendly name. The UI offers Off / Default / enumerated devices independently for system audio and microphone. A selected explicit device disappearing before start is an error; default selection is resolved at start.

## Encoding
When audio is enabled, `MediaStreamSource` is created with a `VideoStreamDescriptor` plus `AudioStreamDescriptor`. `SampleRequested.StreamDescriptor` routes requests to the correct factory. The input audio descriptor is PCM 48 kHz stereo 16-bit. The MP4 output profile uses AAC at a normalized 96-320 kbps bitrate while retaining the existing H.264 video settings.

## Pause/resume and sync
Audio sample timestamps are deterministic `blockIndex * 20ms`. Video timestamps stay `frameIndex / FPS`. Both factories await the same session pause gate. The audio pipeline clears capture buffers on pause and resume. NAudio QPC positions are retained as diagnostics and used to detect large packet gaps, while the media timeline itself is driven by deterministic active-time cadence.

## Recovery
The existing recording manifest schema advances to v2. It persists normalized audio options and audio source selections through the existing `RecordingOptions` object. Older schema-1 journals remain readable; a newer schema is never overwritten by an older app.

## UI
The Recording card gains System audio, Microphone, AAC bitrate, System gain, Mic gain, Refresh devices, and live peak text. Controls are disabled during an active session in the same way as FPS/bitrate/scale. The app does not request or store audio content outside the output MP4/partial file.

## Verification
Core tests cover normalization, cadence, mixing saturation, gains, silence, gap math, and levels. Repository verification must assert the NAudio package, both source paths, two-stream encoder, UI wiring, journal schema v2, and Windows release checklist entries. Windows runtime gates cover default and explicit devices, silence-only loopback, mic unplug, device switch, pause/resume A/V sync, long recording drift, and x64/ARM64 packaging.
