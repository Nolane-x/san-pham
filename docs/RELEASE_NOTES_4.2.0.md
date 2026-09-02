# Magic Capture Desktop 4.2.0 — Webcam / Picture-in-Picture Recording

Magic Capture Desktop 4.2 extends the 4.1 local-native A/V recorder with a bounded webcam capture and Picture-in-Picture pipeline. The webcam path does not create a second recorder: screen frames still come from the 3.9 WGC → Desktop Duplication → GDI router, audio still uses the 4.1 master recording clock, and camera frames are composited immediately before the existing H.264 encoder receives each video sample.

## Webcam capture

- Camera enumeration through Windows `DeviceClass.VideoCapture`.
- Explicit camera selection in the recorder UI with refresh.
- `MediaCapture` initialized for video-only, CPU memory and `SharedReadOnly` access.
- `MediaFrameReader` requests BGRA8 and stores exactly one deep-copied latest frame.
- First-frame warm-up is bounded to five seconds.
- Camera failures, permission denial, unplug/device loss and stale frames fail closed when webcam was requested; Magic Capture never silently downgrades to a non-webcam recording.
- WinRT frame references and `SoftwareBitmap` objects are disposed immediately after deep copy to avoid exhausting the camera frame pool.

## Picture-in-Picture compositor

- Position presets: top-left, top-right, bottom-left, bottom-right, plus custom X/Y.
- Width: 10–50% of output frame.
- Shapes: rectangle, rounded rectangle and true circular crop.
- Horizontal mirror toggle.
- Opacity: 20–100%.
- Optional 0–12 px white border.
- Camera aspect ratio is preserved with deterministic center-crop fill.
- Core compositor uses bounded bilinear BGRA sampling and alpha blending.
- Overlay coordinates are percentages of the encoded output, so PiP placement is stable across recording scale settings.

## Lifecycle and recovery

- Webcam warm-up completes before the active recording clock starts.
- Pause keeps the camera source alive but encodes no new video samples, preserving the existing pause-excluded timeline.
- Schema-v3 recording journal stores the non-secret camera device/overlay options.
- Schema v1/v2 remain readable/writable; future schema v4+ remains read-only.
- `.partial.mp4` promotion rules are unchanged: any requested webcam failure prevents final-file promotion.

## Privacy

The MSIX manifest declares the `webcam` device capability in addition to the existing microphone capability. Camera permission denial is a mandatory Windows release test.

## Source verification

The Linux source-generation environment can run repository, XML/XAML structural and C# lexical gates, but cannot claim a Windows WinUI build or execute xUnit/MediaCapture runtime tests. The Windows release matrix remains mandatory before shipping binaries.
