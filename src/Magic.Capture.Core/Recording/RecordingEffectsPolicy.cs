namespace Magic.Capture.Core.Recording;

public enum RecordingOutputFormat
{
    Mp4,
    Gif,
    Apng,
    M4a
}

public readonly record struct RecordingPoint(int X, int Y);

public readonly record struct RecordingRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

public static class RecordingEffectsPolicy
{
    public const int MinimumZoomPercent = 150;
    public const int MaximumZoomPercent = 300;
    public const int DefaultZoomPercent = 200;
    public const int MaximumStrokePoints = 2048;
    public static readonly TimeSpan RippleLifetime = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan KeyOverlayLifetime = TimeSpan.FromMilliseconds(1200);

    public static bool HasAnyEffect(RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.CursorHighlight
            || options.ClickVisualization
            || options.SafeKeyOverlay
            || options.DrawWhileRecording
            || options.LiveZoom;
    }

    public static bool SupportsAudio(RecordingOutputFormat format) => format is RecordingOutputFormat.Mp4 or RecordingOutputFormat.M4a;

    public static RecordingPoint MapDesktopPointToTarget(int desktopX, int desktopY, RecordingRect target) =>
        new(desktopX - target.X, desktopY - target.Y);

    public static bool TryGetRippleProgress(TimeSpan now, TimeSpan started, out double progress)
    {
        var elapsed = now - started;
        if (elapsed < TimeSpan.Zero || elapsed >= RippleLifetime)
        {
            progress = 0;
            return false;
        }
        progress = Math.Clamp(elapsed.TotalMilliseconds / RippleLifetime.TotalMilliseconds, 0.0, 1.0);
        return true;
    }

    public static RecordingRect ComputeZoomSourceRect(int width, int height, RecordingPoint focus, int zoomPercent)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var zoom = Math.Clamp(zoomPercent, MinimumZoomPercent, MaximumZoomPercent);
        var sourceWidth = Math.Max(1, checked((int)Math.Round(width * 100.0 / zoom)));
        var sourceHeight = Math.Max(1, checked((int)Math.Round(height * 100.0 / zoom)));
        sourceWidth = Math.Min(width, sourceWidth);
        sourceHeight = Math.Min(height, sourceHeight);
        var x = Math.Clamp(focus.X - sourceWidth / 2, 0, width - sourceWidth);
        var y = Math.Clamp(focus.Y - sourceHeight / 2, 0, height - sourceHeight);
        return new RecordingRect(x, y, sourceWidth, sourceHeight);
    }

    public static IReadOnlyList<RecordingPoint> BoundStroke(IReadOnlyList<RecordingPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count <= MaximumStrokePoints) return points.ToArray();
        var result = new RecordingPoint[MaximumStrokePoints];
        var step = (points.Count - 1d) / (MaximumStrokePoints - 1d);
        for (var i = 0; i < MaximumStrokePoints; i++)
            result[i] = points[Math.Min(points.Count - 1, (int)Math.Round(i * step))];
        result[^1] = points[^1];
        return result;
    }
}

public static class RecordingSafeKeyFormatter
{
    private static readonly Dictionary<uint, string> NamedKeys = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Space",
        [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2D] = "Insert", [0x2E] = "Delete"
    };

    public static string? Format(uint virtualKey, bool control, bool alt, bool shift, bool win)
    {
        string? key = null;
        if (virtualKey is >= 0x70 and <= 0x87)
            key = $"F{virtualKey - 0x6F}";
        else if (NamedKeys.TryGetValue(virtualKey, out var named))
            key = named;
        else if (control || alt || win)
        {
            if (virtualKey is >= 0x41 and <= 0x5A) key = ((char)virtualKey).ToString();
            else if (virtualKey is >= 0x30 and <= 0x39) key = ((char)virtualKey).ToString();
        }

        if (key is null) return null;
        var parts = new List<string>(5);
        if (control) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(key);
        return string.Join('+', parts);
    }
}

public static class RecordingOutputPolicy
{
    public static string Extension(RecordingOutputFormat format) => format switch
    {
        RecordingOutputFormat.Mp4 => ".mp4",
        RecordingOutputFormat.Gif => ".gif",
        RecordingOutputFormat.Apng => ".png",
        RecordingOutputFormat.M4a => ".m4a",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string PartialSuffix(RecordingOutputFormat format) => $".partial{Extension(format)}";

    public static string DisplayName(RecordingOutputFormat format) => format switch
    {
        RecordingOutputFormat.Mp4 => "MP4 / H.264",
        RecordingOutputFormat.Gif => "Animated GIF",
        RecordingOutputFormat.Apng => "Animated PNG (APNG)",
        RecordingOutputFormat.M4a => "M4A / AAC audio only",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static bool IsAudioOnly(RecordingOutputFormat format) => format == RecordingOutputFormat.M4a;

    public static void ValidateCompatibility(RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!RecordingEffectsPolicy.SupportsAudio(options.OutputFormat) && (options.IncludeSystemAudio || options.IncludeMicrophone))
            throw new InvalidOperationException($"{DisplayName(options.OutputFormat)} recording is visual-only. Disable system audio and microphone or choose MP4/M4A.");
        if (!IsAudioOnly(options.OutputFormat)) return;
        if (!options.IncludeSystemAudio && !options.IncludeMicrophone)
            throw new InvalidOperationException("M4A audio-only recording requires system audio, microphone, or both.");
        if (options.IncludeWebcam || options.CursorHighlight || options.ClickVisualization || options.SafeKeyOverlay || options.DrawWhileRecording || options.LiveZoom)
            throw new InvalidOperationException("M4A audio-only recording cannot use webcam, cursor, drawing, key, click, or zoom visual effects.");
    }
}
