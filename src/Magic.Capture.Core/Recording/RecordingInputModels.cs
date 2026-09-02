namespace Magic.Capture.Core.Recording;

public enum RecordingMouseButton
{
    Left,
    Right
}

public sealed record RecordingClickEvent(RecordingPoint Point, RecordingMouseButton Button, TimeSpan Timestamp);

public sealed record RecordingStroke(IReadOnlyList<RecordingPoint> Points, TimeSpan Started, TimeSpan Updated);

public sealed record RecordingKeyOverlay(string Label, TimeSpan Timestamp);

public sealed record RecordingInputSnapshot(
    RecordingPoint Cursor,
    IReadOnlyList<RecordingClickEvent> Clicks,
    IReadOnlyList<RecordingStroke> Strokes,
    RecordingKeyOverlay? Key,
    bool ZoomActive)
{
    public static RecordingInputSnapshot Empty { get; } = new(
        new RecordingPoint(-1, -1),
        Array.Empty<RecordingClickEvent>(),
        Array.Empty<RecordingStroke>(),
        null,
        false);
}
