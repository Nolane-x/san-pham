namespace Magic.Capture.Core.Recording;

public enum RecordingTargetKind
{
    Region,
    Window,
    Monitor,
    VirtualDesktop,
    AudioOnly
}

public enum RecordingSessionState
{
    Preparing,
    Recording,
    Paused,
    Finalizing,
    Completed,
    Failed
}

public sealed record RecordingOptions(
    int FramesPerSecond = 30,
    int BitrateMbps = 8,
    int ScalePercent = 100,
    bool IncludeCursor = true,
    int CountdownSeconds = 3,
    int? StopAfterMinutes = null,
    bool IncludeSystemAudio = false,
    bool IncludeMicrophone = false,
    string? SystemAudioDeviceId = null,
    string? MicrophoneDeviceId = null,
    int AudioBitrateKbps = 192,
    int SystemAudioGainPercent = 100,
    int MicrophoneGainPercent = 100,
    bool IncludeWebcam = false,
    string? WebcamDeviceId = null,
    int WebcamXPercent = 100,
    int WebcamYPercent = 100,
    int WebcamWidthPercent = 25,
    WebcamOverlayShape WebcamShape = WebcamOverlayShape.Rounded,
    bool MirrorWebcam = true,
    int WebcamOpacityPercent = 100,
    int WebcamBorderPixels = 2,
    RecordingOutputFormat OutputFormat = RecordingOutputFormat.Mp4,
    bool CursorHighlight = false,
    bool ClickVisualization = false,
    bool SafeKeyOverlay = false,
    bool DrawWhileRecording = false,
    bool LiveZoom = false,
    int ZoomPercent = RecordingEffectsPolicy.DefaultZoomPercent);

public static class RecordingRules
{
    public const int MinimumFramesPerSecond = 5;
    public const int MaximumFramesPerSecond = 60;
    public const int MinimumBitrateMbps = 1;
    public const int MaximumBitrateMbps = 50;
    public const int MinimumScalePercent = 25;
    public const int MaximumScalePercent = 100;
    public const int MinimumCountdownSeconds = 0;
    public const int MaximumCountdownSeconds = 10;
    public const int MinimumStopAfterMinutes = 1;
    public const int MaximumStopAfterMinutes = 240;
    public const int MinimumAudioBitrateKbps = 96;
    public const int MaximumAudioBitrateKbps = 320;
    public const int MinimumAudioGainPercent = 0;
    public const int MaximumAudioGainPercent = 200;
    public const int MaximumAudioDeviceIdLength = 1024;

    public static RecordingOptions Normalize(RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with
        {
            FramesPerSecond = Math.Clamp(options.FramesPerSecond, MinimumFramesPerSecond, MaximumFramesPerSecond),
            BitrateMbps = Math.Clamp(options.BitrateMbps, MinimumBitrateMbps, MaximumBitrateMbps),
            ScalePercent = Math.Clamp(options.ScalePercent, MinimumScalePercent, MaximumScalePercent),
            CountdownSeconds = Math.Clamp(options.CountdownSeconds, MinimumCountdownSeconds, MaximumCountdownSeconds),
            StopAfterMinutes = options.StopAfterMinutes is { } minutes
                ? Math.Clamp(minutes, MinimumStopAfterMinutes, MaximumStopAfterMinutes)
                : null,
            SystemAudioDeviceId = NormalizeDeviceId(options.SystemAudioDeviceId, nameof(options.SystemAudioDeviceId)),
            MicrophoneDeviceId = NormalizeDeviceId(options.MicrophoneDeviceId, nameof(options.MicrophoneDeviceId)),
            AudioBitrateKbps = Math.Clamp(options.AudioBitrateKbps, MinimumAudioBitrateKbps, MaximumAudioBitrateKbps),
            SystemAudioGainPercent = Math.Clamp(options.SystemAudioGainPercent, MinimumAudioGainPercent, MaximumAudioGainPercent),
            MicrophoneGainPercent = Math.Clamp(options.MicrophoneGainPercent, MinimumAudioGainPercent, MaximumAudioGainPercent),
            WebcamDeviceId = RecordingWebcamPolicy.NormalizeDeviceId(options.WebcamDeviceId),
            WebcamXPercent = Math.Clamp(options.WebcamXPercent, 0, 100),
            WebcamYPercent = Math.Clamp(options.WebcamYPercent, 0, 100),
            WebcamWidthPercent = Math.Clamp(options.WebcamWidthPercent, RecordingWebcamPolicy.MinimumWidthPercent, RecordingWebcamPolicy.MaximumWidthPercent),
            WebcamOpacityPercent = Math.Clamp(options.WebcamOpacityPercent, RecordingWebcamPolicy.MinimumOpacityPercent, RecordingWebcamPolicy.MaximumOpacityPercent),
            WebcamBorderPixels = Math.Clamp(options.WebcamBorderPixels, 0, RecordingWebcamPolicy.MaximumBorderPixels),
            ZoomPercent = Math.Clamp(options.ZoomPercent, RecordingEffectsPolicy.MinimumZoomPercent, RecordingEffectsPolicy.MaximumZoomPercent)
        };
    }

    private static string? NormalizeDeviceId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > MaximumAudioDeviceIdLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Audio device id cannot exceed {MaximumAudioDeviceIdLength} characters.");
        return trimmed;
    }

    public static int ScaleDimension(int sourcePixels, int scalePercent)
    {
        if (sourcePixels <= 0) throw new ArgumentOutOfRangeException(nameof(sourcePixels));
        var percent = Math.Clamp(scalePercent, MinimumScalePercent, MaximumScalePercent);
        var scaled = Math.Max(2, checked((int)((long)sourcePixels * percent / 100)));
        if ((scaled & 1) != 0) scaled--;
        return Math.Max(2, scaled);
    }
}

public static class RecordingCadence
{
    public static TimeSpan FrameDuration(int framesPerSecond)
    {
        var fps = Math.Clamp(framesPerSecond, RecordingRules.MinimumFramesPerSecond, RecordingRules.MaximumFramesPerSecond);
        return TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
    }

    public static TimeSpan TimestampForFrame(long frameIndex, int framesPerSecond)
    {
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        var duration = FrameDuration(framesPerSecond);
        return TimeSpan.FromTicks(checked(duration.Ticks * frameIndex));
    }
}

public static class RecordingStopPolicy
{
    public static bool ShouldStop(TimeSpan activeElapsed, int? stopAfterMinutes)
    {
        if (stopAfterMinutes is null) return false;
        var minutes = Math.Clamp(stopAfterMinutes.Value, RecordingRules.MinimumStopAfterMinutes, RecordingRules.MaximumStopAfterMinutes);
        return activeElapsed >= TimeSpan.FromMinutes(minutes);
    }
}

public static class RecordingStateMachine
{
    public static bool CanTransition(RecordingSessionState from, RecordingSessionState to) =>
        (from, to) switch
        {
            (RecordingSessionState.Preparing, RecordingSessionState.Recording) => true,
            (RecordingSessionState.Preparing, RecordingSessionState.Failed) => true,
            (RecordingSessionState.Recording, RecordingSessionState.Paused) => true,
            (RecordingSessionState.Recording, RecordingSessionState.Finalizing) => true,
            (RecordingSessionState.Recording, RecordingSessionState.Failed) => true,
            (RecordingSessionState.Paused, RecordingSessionState.Recording) => true,
            (RecordingSessionState.Paused, RecordingSessionState.Finalizing) => true,
            (RecordingSessionState.Paused, RecordingSessionState.Failed) => true,
            (RecordingSessionState.Finalizing, RecordingSessionState.Completed) => true,
            (RecordingSessionState.Finalizing, RecordingSessionState.Failed) => true,
            _ => false
        };
}

public static class RecordingManifestPolicy
{
    public const int CurrentSchemaVersion = 5;

    public static bool CanReadSchema(int schemaVersion) => schemaVersion >= 0;

    public static bool CanWriteSchema(int schemaVersion) => schemaVersion >= 0 && schemaVersion <= CurrentSchemaVersion;

    public static bool IsUnfinished(RecordingSessionState state) =>
        state is RecordingSessionState.Preparing
            or RecordingSessionState.Recording
            or RecordingSessionState.Paused
            or RecordingSessionState.Finalizing;
}
