namespace Magic.Capture.Core.VideoEditing;

public static class VideoEditRules
{
    public const int MaximumSources = 64;
    public const int MaximumSegments = 256;
    public const int MaximumOverlays = 128;
    public const int MaximumTrackingKeyframes = 256;
    public const int MaximumFrameEffects = VideoEditFrameEffectPolicy.MaximumFrameEffects;
    public const int MaximumOutputDimension = 16_384;
    public const int MaximumOverlayTextLength = 1024;
    public const int MaximumTitleTextLength = 512;
    public const double MaximumVolume = 2.0;
    public const double MaximumOverlayStrokeWidth = 32.0;
    public static readonly TimeSpan MaximumTitleDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumTrackingDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumTimedItemDuration = TimeSpan.FromMilliseconds(50);
    private const double MinimumCropExtent = 0.01;

    public static double NormalizeVolume(double volume) =>
        double.IsFinite(volume) ? Math.Clamp(volume, 0.0, MaximumVolume) : 1.0;

    public static TimeSpan RenderedDuration(TimeSpan sourceDuration, double playbackRate)
    {
        if (sourceDuration <= TimeSpan.Zero) return TimeSpan.Zero;
        var rate = VideoEditFrameEffectPolicy.NormalizePlaybackRate(playbackRate);
        return TimeSpan.FromTicks(Math.Max(1L, checked((long)Math.Round(sourceDuration.Ticks / rate))));
    }

    public static VideoEditCrop NormalizeCrop(VideoEditCrop crop)
    {
        ArgumentNullException.ThrowIfNull(crop);
        var x = double.IsFinite(crop.X) ? Math.Clamp(crop.X, 0.0, 1.0 - MinimumCropExtent) : 0.0;
        var y = double.IsFinite(crop.Y) ? Math.Clamp(crop.Y, 0.0, 1.0 - MinimumCropExtent) : 0.0;
        var width = double.IsFinite(crop.Width) ? Math.Clamp(crop.Width, MinimumCropExtent, 1.0 - x) : 1.0 - x;
        var height = double.IsFinite(crop.Height) ? Math.Clamp(crop.Height, MinimumCropExtent, 1.0 - y) : 1.0 - y;
        return new VideoEditCrop(x, y, width, height);
    }

    public static int NormalizeOutputDimension(int dimension)
    {
        var bounded = Math.Clamp(dimension, 2, MaximumOutputDimension);
        if ((bounded & 1) != 0) bounded--;
        return Math.Max(2, bounded);
    }

    public static VideoEditSegment Trim(VideoEditSegment segment, TimeSpan sourceStart, TimeSpan sourceEnd, TimeSpan sourceDuration)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.IsTitleCard) throw new InvalidOperationException("Title-card segments do not have source trim ranges.");
        if (sourceDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceDuration));
        if (sourceStart < TimeSpan.Zero || sourceEnd <= sourceStart || sourceEnd > sourceDuration)
            throw new ArgumentOutOfRangeException(nameof(sourceEnd), "Trim range must be non-empty and inside the source duration.");

        return segment with
        {
            SourceStart = sourceStart,
            SourceEnd = sourceEnd,
            Volume = NormalizeVolume(segment.Volume),
            Crop = segment.Crop is null ? null : NormalizeCrop(segment.Crop)
        };
    }

    public static IReadOnlyList<VideoEditSegment> CutOut(VideoEditSegment segment, TimeSpan relativeStart, TimeSpan relativeEnd)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.IsTitleCard) throw new InvalidOperationException("Title-card segments cannot be middle-cut with source-range semantics.");
        if (segment.Duration <= TimeSpan.Zero) throw new ArgumentException("Segment duration must be positive.", nameof(segment));
        if (relativeStart < TimeSpan.Zero || relativeEnd <= relativeStart || relativeEnd > segment.Duration)
            throw new ArgumentOutOfRangeException(nameof(relativeEnd), "Cut range must be non-empty and inside the segment.");

        var cutStart = segment.SourceStart + relativeStart;
        var cutEnd = segment.SourceStart + relativeEnd;
        var result = new List<VideoEditSegment>(2);
        if (cutStart > segment.SourceStart)
            result.Add(segment with { SourceEnd = cutStart });
        if (cutEnd < segment.SourceEnd)
            result.Add(segment with { SourceStart = cutEnd });
        return result;
    }

    public static TimeSpan TimelineDuration(IEnumerable<VideoEditSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        long ticks = 0;
        foreach (var segment in segments)
        {
            if (segment.Duration <= TimeSpan.Zero) continue;
            ticks = checked(ticks + segment.RenderedDuration.Ticks);
        }
        return TimeSpan.FromTicks(ticks);
    }

    public static IReadOnlyList<string> ValidateProject(VideoEditProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var errors = new List<string>();
        if (!VideoEditProjectSchema.CanRead(project.SchemaVersion))
            errors.Add($"Unsupported clip-project schema {project.SchemaVersion}.");
        if (project.Sources.Count > MaximumSources) errors.Add($"Project exceeds {MaximumSources} sources.");
        if (project.Segments.Count == 0) errors.Add("Project has no timeline segments.");
        if (project.Segments.Count > MaximumSegments) errors.Add($"Project exceeds {MaximumSegments} segments.");
        if (project.OutputWidth != NormalizeOutputDimension(project.OutputWidth) || project.OutputHeight != NormalizeOutputDimension(project.OutputHeight))
            errors.Add("Output dimensions must be even and within the supported H.264 bounds.");

        var sourceMap = new Dictionary<string, VideoEditSource>(StringComparer.Ordinal);
        foreach (var source in project.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id)) { errors.Add("A source has an empty id."); continue; }
            if (!sourceMap.TryAdd(source.Id, source)) { errors.Add($"Duplicate source id '{source.Id}'."); continue; }
            if (string.IsNullOrWhiteSpace(source.Path) || !Path.IsPathFullyQualified(source.Path))
                errors.Add($"Source '{source.Id}' path must be fully qualified.");
            if (source.Duration <= TimeSpan.Zero) errors.Add($"Source '{source.Id}' duration must be positive.");
            if (source.Width <= 0 || source.Height <= 0) errors.Add($"Source '{source.Id}' dimensions must be positive.");
        }

        for (var i = 0; i < project.Segments.Count; i++)
        {
            var segment = project.Segments[i];
            if (segment.IsTitleCard)
            {
                ValidateTitleCard(segment.TitleCard!, i, errors);
                continue;
            }

            if (!sourceMap.TryGetValue(segment.SourceId, out var source))
            {
                errors.Add($"Segment {i + 1} references unknown source '{segment.SourceId}'.");
                continue;
            }
            if (segment.SourceStart < TimeSpan.Zero || segment.SourceEnd <= segment.SourceStart || segment.SourceEnd > source.Duration)
                errors.Add($"Segment {i + 1} has an invalid source range.");
            if (!double.IsFinite(segment.Volume) || segment.Volume < 0.0 || segment.Volume > MaximumVolume)
                errors.Add($"Segment {i + 1} volume is outside 0–200%.");
            if (segment.Crop is { } crop && !CropIsNormalized(crop))
                errors.Add($"Segment {i + 1} crop is outside the normalized source canvas.");
            if (!double.IsFinite(segment.PlaybackRate) || segment.PlaybackRate < VideoEditFrameEffectPolicy.MinimumPlaybackRate || segment.PlaybackRate > VideoEditFrameEffectPolicy.MaximumPlaybackRate)
                errors.Add($"Segment {i + 1} playback rate is outside 0.25x–4x.");
            ValidateAudioEnvelope(segment, i, errors);
        }

        if (project.Sources.Count == 0 && !project.Segments.Any(x => x.IsTitleCard))
            errors.Add("Project has no media sources.");

        if (project.OutputFramesPerSecond != VideoEditFrameEffectPolicy.NormalizeOutputFps(project.OutputFramesPerSecond))
            errors.Add("Output FPS must be one of 15, 24, 30 or 60.");
        ValidateOverlays(project, errors);
        ValidateFrameEffects(project, errors);
        return errors;
    }

    private static void ValidateTitleCard(VideoEditTitleCard title, int index, ICollection<string> errors)
    {
        if (title.Duration < MinimumTimedItemDuration || title.Duration > MaximumTitleDuration)
            errors.Add($"Title card {index + 1} duration is outside the supported range.");
        if (string.IsNullOrWhiteSpace(title.Text) || title.Text.Length > MaximumTitleTextLength)
            errors.Add($"Title card {index + 1} text must contain 1–{MaximumTitleTextLength} characters.");
        if (!double.IsFinite(title.FontScale) || title.FontScale < 0.02 || title.FontScale > 0.25)
            errors.Add($"Title card {index + 1} font scale is outside 2–25% of output height.");
        ValidateTextStyle(title.TextStyle, $"Title card {index + 1}", errors);
    }

    private static void ValidateOverlays(VideoEditProject project, ICollection<string> errors)
    {
        var overlays = project.OverlayItems;
        if (overlays.Count > MaximumOverlays) errors.Add($"Project exceeds {MaximumOverlays} overlays.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var timelineDuration = project.TimelineDuration;
        foreach (var overlay in overlays)
        {
            if (string.IsNullOrWhiteSpace(overlay.Id) || !ids.Add(overlay.Id))
                errors.Add("Overlay ids must be non-empty and unique.");
            if (overlay.Start < TimeSpan.Zero || overlay.Duration < MinimumTimedItemDuration || overlay.End > timelineDuration)
                errors.Add($"Overlay '{overlay.Id}' timing is outside the project timeline.");
            if (!CropIsNormalized(overlay.Bounds))
                errors.Add($"Overlay '{overlay.Id}' bounds are outside the normalized canvas.");
            if (!double.IsFinite(overlay.Opacity) || overlay.Opacity < 0 || overlay.Opacity > 1)
                errors.Add($"Overlay '{overlay.Id}' opacity is outside 0–100%.");
            if (!double.IsFinite(overlay.StrokeWidth) || overlay.StrokeWidth < 0 || overlay.StrokeWidth > MaximumOverlayStrokeWidth)
                errors.Add($"Overlay '{overlay.Id}' stroke width is outside the supported range.");
            if (!double.IsFinite(overlay.FontScale) || overlay.FontScale < 0.01 || overlay.FontScale > 0.25)
                errors.Add($"Overlay '{overlay.Id}' font scale is outside the supported range.");
            if (overlay.Text.Length > MaximumOverlayTextLength)
                errors.Add($"Overlay '{overlay.Id}' text exceeds {MaximumOverlayTextLength} characters.");
            if (overlay.Kind == VideoEditOverlayKind.Text && string.IsNullOrWhiteSpace(overlay.Text))
                errors.Add($"Text overlay '{overlay.Id}' is empty.");
            if (overlay.Kind == VideoEditOverlayKind.Text) ValidateTextStyle(overlay.TextStyle, $"Overlay '{overlay.Id}'", errors);

            var keyframes = overlay.Keyframes ?? Array.Empty<VideoEditOverlayKeyframe>();
            if (keyframes.Count > MaximumTrackingKeyframes)
                errors.Add($"Overlay '{overlay.Id}' exceeds the {MaximumTrackingKeyframes} tracking keyframe limit.");
            var previous = TimeSpan.MinValue;
            foreach (var keyframe in keyframes)
            {
                if (keyframe.Offset < TimeSpan.Zero || keyframe.Offset > overlay.Duration || keyframe.Offset <= previous)
                    errors.Add($"Overlay '{overlay.Id}' keyframe timing is invalid.");
                if (!CropIsNormalized(keyframe.Bounds))
                    errors.Add($"Overlay '{overlay.Id}' keyframe bounds are outside the normalized canvas.");
                if (!double.IsFinite(keyframe.Opacity) || keyframe.Opacity < 0 || keyframe.Opacity > 1)
                    errors.Add($"Overlay '{overlay.Id}' keyframe opacity is outside 0–100%.");
                previous = keyframe.Offset;
            }
        }
    }


    private static void ValidateAudioEnvelope(VideoEditSegment segment, int index, ICollection<string> errors)
    {
        var keyframes = segment.AudioEnvelope?.Keyframes ?? Array.Empty<VideoEditAudioKeyframe>();
        if (keyframes.Count > VideoEditAudioEnvelopePolicy.MaximumKeyframesPerSegment)
            errors.Add($"Segment {index + 1} exceeds the {VideoEditAudioEnvelopePolicy.MaximumKeyframesPerSegment} audio-envelope keyframe limit.");
        var previous = TimeSpan.MinValue;
        foreach (var keyframe in keyframes)
        {
            if (keyframe.Offset < TimeSpan.Zero || keyframe.Offset > segment.RenderedDuration || keyframe.Offset <= previous)
                errors.Add($"Segment {index + 1} audio-envelope keyframe timing is invalid.");
            if (!double.IsFinite(keyframe.Gain) || keyframe.Gain < 0 || keyframe.Gain > MaximumVolume)
                errors.Add($"Segment {index + 1} audio-envelope gain is outside 0–200%.");
            previous = keyframe.Offset;
        }
    }

    private static void ValidateTextStyle(VideoEditTextStyle? style, string label, ICollection<string> errors)
    {
        if (style is null) return;
        var normalized = VideoEditTextStyle.Normalize(style);
        if (!string.Equals(normalized.FontFamily, style.FontFamily?.Trim(), StringComparison.Ordinal) ||
            normalized.Weight != style.Weight || Math.Abs(normalized.ShadowOffset - style.ShadowOffset) > 1e-9 ||
            Math.Abs(normalized.OutlineWidth - style.OutlineWidth) > 1e-9)
            errors.Add($"{label} text style is outside supported bounds.");
    }

    private static void ValidateFrameEffects(VideoEditProject project, ICollection<string> errors)
    {
        var effects = project.FrameEffectItems;
        if (effects.Count > MaximumFrameEffects) errors.Add($"Project exceeds {MaximumFrameEffects} frame effects.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            if (string.IsNullOrWhiteSpace(effect.Id) || !ids.Add(effect.Id)) errors.Add("Frame-effect ids must be non-empty and unique.");
            if (effect.Start < TimeSpan.Zero || effect.Duration <= TimeSpan.Zero || effect.End > project.TimelineDuration)
                errors.Add($"Frame effect '{effect.Id}' timing is outside the rendered timeline.");
            if (effect.Keyframes.Count == 0 || effect.Keyframes.Count > VideoEditFrameEffectPolicy.MaximumKeyframesPerEffect)
                errors.Add($"Frame effect '{effect.Id}' keyframe count is invalid.");
            var previous = TimeSpan.MinValue;
            foreach (var keyframe in effect.Keyframes)
            {
                if (keyframe.Offset < TimeSpan.Zero || keyframe.Offset > effect.Duration || keyframe.Offset <= previous)
                    errors.Add($"Frame effect '{effect.Id}' keyframe timing is invalid.");
                previous = keyframe.Offset;
            }
        }
        if (project.TimelineDuration > VideoEditFrameEffectPolicy.MaximumAdvancedRenderDuration && VideoEditFrameEffectPolicy.RequiresAdvancedRender(project))
            errors.Add("Advanced render timeline exceeds the 4-hour hard limit.");
        var frameCount = project.TimelineDuration.TotalSeconds * VideoEditFrameEffectPolicy.NormalizeOutputFps(project.OutputFramesPerSecond);
        if (frameCount > VideoEditFrameEffectPolicy.MaximumAdvancedRenderFrames && VideoEditFrameEffectPolicy.RequiresAdvancedRender(project))
            errors.Add("Advanced render exceeds the 500,000-frame hard limit.");
    }

    private static bool CropIsNormalized(VideoEditCrop crop)
    {
        var normalized = NormalizeCrop(crop);
        return Math.Abs(normalized.X - crop.X) <= 1e-9 && Math.Abs(normalized.Y - crop.Y) <= 1e-9 &&
               Math.Abs(normalized.Width - crop.Width) <= 1e-9 && Math.Abs(normalized.Height - crop.Height) <= 1e-9;
    }
}
