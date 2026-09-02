namespace Magic.Capture.Core.VideoEditing;

public enum VideoEditEasingKind
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Hold
}

public static class VideoEditEasing
{
    public static double Apply(VideoEditEasingKind kind, double value)
    {
        var t = double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
        return kind switch
        {
            VideoEditEasingKind.Linear => t,
            VideoEditEasingKind.EaseIn => t * t,
            VideoEditEasingKind.EaseOut => 1.0 - (1.0 - t) * (1.0 - t),
            VideoEditEasingKind.EaseInOut => t < 0.5 ? 2.0 * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 2.0) / 2.0,
            VideoEditEasingKind.Hold => t >= 1.0 ? 1.0 : 0.0,
            _ => t
        };
    }

    public static double Lerp(double from, double to, double progress, VideoEditEasingKind easing) =>
        from + (to - from) * Apply(easing, progress);
}

public enum VideoEditTextAlignment
{
    Left,
    Center,
    Right
}

public sealed record VideoEditTextStyle(
    string FontFamily = "Segoe UI",
    int Weight = 700,
    bool Italic = false,
    bool Underline = false,
    VideoEditTextAlignment HorizontalAlignment = VideoEditTextAlignment.Center,
    uint ShadowArgb = 0x00000000u,
    double ShadowOffset = 0.0,
    uint OutlineArgb = 0xFF000000u,
    double OutlineWidth = 0.0)
{
    public const int MinimumWeight = 100;
    public const int MaximumWeight = 900;
    public const double MaximumShadowOffset = 16.0;
    public const double MaximumOutlineWidth = 12.0;
    public const int MaximumFontFamilyLength = 96;

    public static VideoEditTextStyle Normalize(VideoEditTextStyle? style)
    {
        var value = style ?? new VideoEditTextStyle();
        var family = string.IsNullOrWhiteSpace(value.FontFamily) ? "Segoe UI" : value.FontFamily.Trim();
        if (family.Length > MaximumFontFamilyLength) family = family[..MaximumFontFamilyLength];
        var weight = Math.Clamp(value.Weight, MinimumWeight, MaximumWeight);
        weight = checked((int)Math.Round(weight / 100.0) * 100);
        return value with
        {
            FontFamily = family,
            Weight = Math.Clamp(weight, MinimumWeight, MaximumWeight),
            ShadowOffset = Math.Clamp(double.IsFinite(value.ShadowOffset) ? value.ShadowOffset : 0.0, 0.0, MaximumShadowOffset),
            OutlineWidth = Math.Clamp(double.IsFinite(value.OutlineWidth) ? value.OutlineWidth : 0.0, 0.0, MaximumOutlineWidth)
        };
    }
}

public readonly record struct VideoEditOverlayAnimationValue(VideoEditCrop Bounds, double Opacity);

public static class VideoEditOverlayAnimationPolicy
{
    public const int MaximumAnimatedOverlayPieces = 2048;
    public const int MaximumAnimationSamplesPerSecond = 12;

    public static VideoEditOverlayAnimationValue Evaluate(VideoEditOverlay overlay, TimeSpan timelinePosition)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var keyframes = overlay.Keyframes ?? Array.Empty<VideoEditOverlayKeyframe>();
        if (keyframes.Count == 0)
            return new VideoEditOverlayAnimationValue(VideoEditRules.NormalizeCrop(overlay.Bounds), Math.Clamp(overlay.Opacity, 0.0, 1.0));

        var local = timelinePosition - overlay.Start;
        if (local <= keyframes[0].Offset) return Normalize(keyframes[0]);
        if (local >= keyframes[^1].Offset) return Normalize(keyframes[^1]);

        for (var i = 1; i < keyframes.Count; i++)
        {
            var right = keyframes[i];
            if (local > right.Offset) continue;
            var left = keyframes[i - 1];
            var spanTicks = right.Offset.Ticks - left.Offset.Ticks;
            var raw = spanTicks <= 0 ? 0.0 : (local.Ticks - left.Offset.Ticks) / (double)spanTicks;
            var t = VideoEditEasing.Apply(left.Easing, raw);
            var bounds = new VideoEditCrop(
                Lerp(left.Bounds.X, right.Bounds.X, t),
                Lerp(left.Bounds.Y, right.Bounds.Y, t),
                Lerp(left.Bounds.Width, right.Bounds.Width, t),
                Lerp(left.Bounds.Height, right.Bounds.Height, t));
            return new VideoEditOverlayAnimationValue(
                VideoEditRules.NormalizeCrop(bounds),
                Math.Clamp(Lerp(left.Opacity, right.Opacity, t), 0.0, 1.0));
        }

        return Normalize(keyframes[^1]);
    }

    public static IReadOnlyList<(TimeSpan Start, TimeSpan Duration, VideoEditOverlayAnimationValue Value)> BuildPieces(
        VideoEditOverlay overlay,
        int outputFramesPerSecond,
        int remainingBudget)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (remainingBudget <= 0) throw new InvalidDataException("Animated overlay render-piece budget is exhausted.");
        if (overlay.Duration <= TimeSpan.Zero) return Array.Empty<(TimeSpan, TimeSpan, VideoEditOverlayAnimationValue)>();
        var keyframes = overlay.Keyframes ?? Array.Empty<VideoEditOverlayKeyframe>();
        if (keyframes.Count == 0)
        {
            return [(overlay.Start, overlay.Duration, new VideoEditOverlayAnimationValue(VideoEditRules.NormalizeCrop(overlay.Bounds), Math.Clamp(overlay.Opacity, 0.0, 1.0)))];
        }

        var samplesPerSecond = Math.Clamp(outputFramesPerSecond, 1, MaximumAnimationSamplesPerSecond);
        var desired = Math.Max(1, (int)Math.Ceiling(overlay.Duration.TotalSeconds * samplesPerSecond));
        var count = Math.Min(Math.Min(desired, remainingBudget), MaximumAnimatedOverlayPieces);
        var result = new List<(TimeSpan, TimeSpan, VideoEditOverlayAnimationValue)>(count);
        for (var i = 0; i < count; i++)
        {
            var startTicks = checked(overlay.Duration.Ticks * i / count);
            var endTicks = checked(overlay.Duration.Ticks * (i + 1L) / count);
            var start = TimeSpan.FromTicks(startTicks);
            var end = TimeSpan.FromTicks(endTicks);
            if (end <= start) continue;
            result.Add((overlay.Start + start, end - start, Evaluate(overlay, overlay.Start + start)));
        }
        return result;
    }

    private static VideoEditOverlayAnimationValue Normalize(VideoEditOverlayKeyframe keyframe) =>
        new(VideoEditRules.NormalizeCrop(keyframe.Bounds), Math.Clamp(double.IsFinite(keyframe.Opacity) ? keyframe.Opacity : 1.0, 0.0, 1.0));

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);
}

public sealed record VideoEditAudioKeyframe(
    TimeSpan Offset,
    double Gain,
    VideoEditEasingKind Easing = VideoEditEasingKind.Linear);

public sealed record VideoEditAudioEnvelope(IReadOnlyList<VideoEditAudioKeyframe> Keyframes)
{
    public static VideoEditAudioEnvelope CreateFadeAndDuck(
        TimeSpan duration,
        TimeSpan fadeIn,
        TimeSpan fadeOut,
        TimeSpan? duckStart = null,
        TimeSpan? duckEnd = null,
        double duckGain = 0.35)
    {
        if (duration <= TimeSpan.Zero) return new VideoEditAudioEnvelope(Array.Empty<VideoEditAudioKeyframe>());
        var points = new SortedDictionary<long, VideoEditAudioKeyframe>();
        void Add(TimeSpan offset, double gain, VideoEditEasingKind easing = VideoEditEasingKind.Linear)
        {
            var safe = TimeSpan.FromTicks(Math.Clamp(offset.Ticks, 0L, duration.Ticks));
            points[safe.Ticks] = new VideoEditAudioKeyframe(safe, VideoEditRules.NormalizeVolume(gain), easing);
        }

        if (fadeIn > TimeSpan.Zero) { Add(TimeSpan.Zero, 0); Add(fadeIn, 1); }
        else Add(TimeSpan.Zero, 1);
        if (duckStart is { } ds && duckEnd is { } de && de > ds)
        {
            Add(ds, 1, VideoEditEasingKind.EaseOut);
            Add(ds + TimeSpan.FromMilliseconds(Math.Min(150, Math.Max(1, (de - ds).TotalMilliseconds / 4))), duckGain);
            Add(de - TimeSpan.FromMilliseconds(Math.Min(150, Math.Max(1, (de - ds).TotalMilliseconds / 4))), duckGain, VideoEditEasingKind.EaseIn);
            Add(de, 1);
        }
        var fadeStart = duration - fadeOut;
        if (fadeOut > TimeSpan.Zero) { Add(fadeStart, 1, VideoEditEasingKind.EaseIn); Add(duration, 0); }
        else Add(duration, 1);
        return new VideoEditAudioEnvelope(points.Values.ToArray());
    }
}

public static class VideoEditAudioEnvelopePolicy
{
    public const int MaximumKeyframesPerSegment = 128;

    public static double Evaluate(VideoEditAudioEnvelope? envelope, TimeSpan localPosition)
    {
        var keyframes = envelope?.Keyframes ?? Array.Empty<VideoEditAudioKeyframe>();
        if (keyframes.Count == 0) return 1.0;
        if (localPosition <= keyframes[0].Offset) return NormalizeGain(keyframes[0].Gain);
        if (localPosition >= keyframes[^1].Offset) return NormalizeGain(keyframes[^1].Gain);
        for (var i = 1; i < keyframes.Count; i++)
        {
            var right = keyframes[i];
            if (localPosition > right.Offset) continue;
            var left = keyframes[i - 1];
            var spanTicks = right.Offset.Ticks - left.Offset.Ticks;
            var raw = spanTicks <= 0 ? 0.0 : (localPosition.Ticks - left.Offset.Ticks) / (double)spanTicks;
            return NormalizeGain(VideoEditEasing.Lerp(left.Gain, right.Gain, raw, left.Easing));
        }
        return NormalizeGain(keyframes[^1].Gain);
    }

    public static double NormalizeGain(double gain) => VideoEditRules.NormalizeVolume(gain);
}
