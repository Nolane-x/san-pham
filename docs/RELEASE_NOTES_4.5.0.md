# Magic Capture Desktop 4.5.0 — Advanced Clip Editor

Magic Capture Desktop 4.5 extends the non-destructive Clip Editor with timed title/overlay/redaction workflows, bounded automatic redaction tracking, composed-timeline audio extraction and local Windows MediaTranscoder format conversion. Recorder/capture runtime remains isolated.

## Added

- `.magicclip` schema v2 with in-memory migration from schema v1 and future-schema read-only protection.
- Title-card timeline segments with bounded duration and text length.
- Timed text, rectangle, ellipse and arrow overlays with normalized geometry, opacity and stroke controls.
- Timed redaction overlays rendered as solid-color media overlays.
- Content-addressed SHA-256 PNG overlay cache with 64 MiB / 256-file bounds.
- Automatic redaction tracking using bounded luma template matching over base-composition thumbnails; tracking stops on low confidence rather than jumping targets.
- Audio extraction from the composed timeline to WAV, MP3 or M4A.
- Video conversion from the composed timeline to H.264 MP4, HEVC MP4 or WMV through Windows MediaTranscoder.
- Transaction-like partial output staging and non-empty verification before final promotion for extraction/conversion.
- Clip Editor controls for title cards, timed overlays, auto-track, audio extraction and video conversion.

## Source-truth audit

- Done: **376**
- Partial: **64**
- Foundation: **139**
- Missing: **59**
- ReleaseTest: **22**
- Total: **660**

Promoted to Done: #92, #94, #95, #97, #98 and #99. Playback speed #89 and post-record zoom #96 remain Missing.

## Verification boundary

Static/source/package verification is executable in the current environment. A real Windows x64/ARM64 build, xUnit execution, XAML compilation, MediaComposition overlay rendering, tracker visual-quality validation and MediaTranscoder codec availability still require the Windows release matrix.
