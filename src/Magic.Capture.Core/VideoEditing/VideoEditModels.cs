namespace Magic.Capture.Core.VideoEditing;

public static class VideoEditProjectSchema
{
    public const int CurrentVersion = 4;

    public static bool CanRead(int schemaVersion) => schemaVersion >= 1;
    public static bool CanWrite(int schemaVersion) => schemaVersion == CurrentVersion;
}

public enum VideoEditOverlayKind
{
    Text,
    Rectangle,
    Ellipse,
    Arrow,
    Redaction
}

public sealed record VideoEditSource(
    string Id,
    string Path,
    TimeSpan Duration,
    int Width,
    int Height);

public sealed record VideoEditCrop(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record VideoEditTitleCard(
    string Text,
    TimeSpan Duration,
    uint BackgroundArgb = 0xFF101218u,
    uint ForegroundArgb = 0xFFFFFFFFu,
    double FontScale = 0.075,
    VideoEditTextStyle? TextStyle = null);

public sealed record VideoEditOverlayKeyframe(
    TimeSpan Offset,
    VideoEditCrop Bounds,
    double Opacity = 1.0,
    VideoEditEasingKind Easing = VideoEditEasingKind.Linear);

public sealed record VideoEditOverlay(
    string Id,
    VideoEditOverlayKind Kind,
    TimeSpan Start,
    TimeSpan Duration,
    VideoEditCrop Bounds,
    double Opacity = 1.0,
    uint FillArgb = 0xD9000000u,
    uint StrokeArgb = 0xFFFFFFFFu,
    double StrokeWidth = 3.0,
    string Text = "",
    double FontScale = 0.055,
    IReadOnlyList<VideoEditOverlayKeyframe>? Keyframes = null,
    VideoEditTextStyle? TextStyle = null)
{
    public TimeSpan End => Start + Duration;
}

public sealed record VideoEditSegment(
    string SourceId,
    TimeSpan SourceStart,
    TimeSpan SourceEnd,
    double Volume = 1.0,
    VideoEditCrop? Crop = null,
    VideoEditTitleCard? TitleCard = null,
    double PlaybackRate = 1.0,
    VideoEditAudioEnvelope? AudioEnvelope = null)
{
    public bool IsTitleCard => TitleCard is not null;
    public TimeSpan Duration => TitleCard?.Duration ?? (SourceEnd - SourceStart);
    public TimeSpan RenderedDuration => VideoEditRules.RenderedDuration(Duration, PlaybackRate);
    public bool IsMuted => Volume <= 0.000001;
}

public sealed record VideoEditProject(
    IReadOnlyList<VideoEditSource> Sources,
    IReadOnlyList<VideoEditSegment> Segments,
    int OutputWidth,
    int OutputHeight,
    int SchemaVersion = VideoEditProjectSchema.CurrentVersion,
    IReadOnlyList<VideoEditOverlay>? Overlays = null,
    int OutputFramesPerSecond = 30,
    IReadOnlyList<VideoEditFrameEffect>? FrameEffects = null)
{
    public TimeSpan TimelineDuration => VideoEditRules.TimelineDuration(Segments);
    public IReadOnlyList<VideoEditOverlay> OverlayItems => Overlays ?? Array.Empty<VideoEditOverlay>();
    public IReadOnlyList<VideoEditFrameEffect> FrameEffectItems => FrameEffects ?? Array.Empty<VideoEditFrameEffect>();
}

public static class VideoEditProjectMigration
{
    public static VideoEditProject UpgradeToCurrent(VideoEditProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!VideoEditProjectSchema.CanRead(project.SchemaVersion))
            throw new InvalidDataException($"Clip-project schema {project.SchemaVersion} is unsupported.");
        if (project.SchemaVersion > VideoEditProjectSchema.CurrentVersion)
            return project;

        return project with
        {
            SchemaVersion = VideoEditProjectSchema.CurrentVersion,
            Overlays = project.Overlays ?? Array.Empty<VideoEditOverlay>(),
            OutputFramesPerSecond = VideoEditFrameEffectPolicy.NormalizeOutputFps(project.OutputFramesPerSecond),
            FrameEffects = project.FrameEffects ?? Array.Empty<VideoEditFrameEffect>()
        };
    }
}

public sealed record VideoContactSheetPlan(
    int FrameCount,
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight,
    int CanvasWidth,
    int CanvasHeight,
    long RequiredBgraBytes,
    IReadOnlyList<TimeSpan> Timestamps)
{
    public const int MaximumFrames = 64;
    public const long MaximumBgraBytes = 256L * 1024 * 1024;
    public const int MaximumCellWidth = 1280;
    public const int MaximumCellHeight = 720;

    public static VideoContactSheetPlan Create(TimeSpan duration, int requestedFrameCount, int requestedCellWidth, int requestedCellHeight)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        var frameCount = Math.Clamp(requestedFrameCount, 1, MaximumFrames);
        var cellWidth = Math.Clamp(requestedCellWidth, 32, MaximumCellWidth);
        var cellHeight = Math.Clamp(requestedCellHeight, 18, MaximumCellHeight);
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(frameCount)));
        var rows = checked((frameCount + columns - 1) / columns);
        var canvasWidth = checked(columns * cellWidth);
        var canvasHeight = checked(rows * cellHeight);
        var requiredBytes = checked((long)canvasWidth * canvasHeight * 4L);
        if (requiredBytes > MaximumBgraBytes)
            throw new InvalidOperationException("Contact sheet exceeds the bounded BGRA allocation budget.");

        var timestamps = new TimeSpan[frameCount];
        if (frameCount == 1 || duration == TimeSpan.Zero)
        {
            timestamps[0] = TimeSpan.Zero;
        }
        else
        {
            var lastTick = Math.Max(0L, duration.Ticks - 1L);
            for (var i = 0; i < frameCount; i++)
                timestamps[i] = TimeSpan.FromTicks(checked(lastTick * i / (frameCount - 1L)));
        }

        return new VideoContactSheetPlan(
            frameCount,
            columns,
            rows,
            cellWidth,
            cellHeight,
            canvasWidth,
            canvasHeight,
            requiredBytes,
            timestamps);
    }
}
