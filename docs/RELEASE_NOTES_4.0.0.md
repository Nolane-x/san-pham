# Magic Capture Desktop 4.0.0 — Local Visual Recording

Magic Capture Desktop 4.0.0 adds the first recording subsystem as a separate, local-native pipeline. It deliberately reuses the 3.9 capture router instead of creating a second screenshot engine, so recording inherits the same physical-pixel target model and WGC → Desktop Duplication → GDI fallback policy.

## Added

- Region, window, monitor and full virtual-desktop visual recording.
- Local MP4/H.264 output through Windows `MediaStreamSource` + `MediaTranscoder`; no bundled FFmpeg process and no network service.
- Configurable 5–60 FPS, 1–50 Mbps bitrate, 25–100% scale, cursor inclusion, 0–10 second countdown and optional 1–240 minute stop limit.
- Even-dimension normalization for H.264 output.
- Pause/resume with paused wall time removed from the encoded timeline.
- Elapsed-time and frame-count progress in the Home recording card.
- `WDA_EXCLUDEFROMCAPTURE` on the top-level recording-control window. Recording start fails closed if Windows cannot apply the exclusion policy.
- Same-directory `.partial.mp4` output promoted to the requested `.mp4` only after clean transcode completion.
- Atomic recording session journal with future-schema read-only protection and startup detection of unfinished sessions.
- Repeat-last-recording-region within the active app session.
- Capture target invalidation: window size, monitor resolution or virtual desktop layout changes stop the session instead of silently stretching stale/incorrect frames.

## Deliberately not claimed complete

- Audio capture, microphone mixing, webcam/PiP.
- Selectable multi-monitor subsets (full virtual desktop is supported).
- HEVC, AV1, VP9/WebM, GIF, animated WebP/APNG.
- Cursor highlight/click effects, drawing, live zoom and post-recording timeline editing.
- Guaranteed hardware encoding. 4.0 requests `MediaTranscoder.HardwareAccelerationEnabled`; actual hardware/driver use remains a Windows runtime gate.
- Reconstructing or finalizing a crash-interrupted `.partial.mp4`. 4.0 detects and preserves it but does not label it playable or rename it automatically.

## Source-truth audit

- Done: **349**
- Partial: **63**
- Foundation: **139**
- Missing: **87**
- ReleaseTest: **22**
- Total: **660**

## Verification boundary

Repository, structural, lexical, audit, XML and deterministic source-bundle verification can run in the current environment. This environment does not provide the .NET/WinUI/MSIX Windows build toolchain, Windows Media codec runtime, GPU drivers or Windows capture APIs; xUnit execution, compilation, MediaTranscoder behavior, hardware-encoder selection and long-run recording remain Windows release gates.
