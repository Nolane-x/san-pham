namespace Magic.Capture.Core.Recording;

public static class RecordingAudioPolicy
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int BytesPerSample = BitsPerSample / 8;
    public const int BlockMilliseconds = 20;
    public const int FramesPerBlock = SampleRate * BlockMilliseconds / 1000;
    public const int SamplesPerBlock = FramesPerBlock * Channels;
    public const int BytesPerBlock = SamplesPerBlock * BytesPerSample;
    public const int MaximumBufferedSeconds = 2;
    public const int MaximumBufferedBytes = SampleRate * Channels * BytesPerSample * MaximumBufferedSeconds;

    public static TimeSpan BlockDuration => TimeSpan.FromMilliseconds(BlockMilliseconds);

    public static TimeSpan TimestampForBlock(long blockIndex)
    {
        if (blockIndex < 0) throw new ArgumentOutOfRangeException(nameof(blockIndex));
        return TimeSpan.FromTicks(checked(BlockDuration.Ticks * blockIndex));
    }
}

public static class RecordingAudioMixer
{
    public static void MixPcm16(
        ReadOnlySpan<short> system,
        ReadOnlySpan<short> microphone,
        Span<short> output,
        int systemGainPercent,
        int microphoneGainPercent)
    {
        systemGainPercent = Math.Clamp(systemGainPercent, RecordingRules.MinimumAudioGainPercent, RecordingRules.MaximumAudioGainPercent);
        microphoneGainPercent = Math.Clamp(microphoneGainPercent, RecordingRules.MinimumAudioGainPercent, RecordingRules.MaximumAudioGainPercent);
        if (!system.IsEmpty && system.Length < output.Length) throw new ArgumentException("System-audio input is shorter than the output block.", nameof(system));
        if (!microphone.IsEmpty && microphone.Length < output.Length) throw new ArgumentException("Microphone input is shorter than the output block.", nameof(microphone));

        for (var i = 0; i < output.Length; i++)
        {
            var left = system.IsEmpty ? 0 : Scale(system[i], systemGainPercent);
            var right = microphone.IsEmpty ? 0 : Scale(microphone[i], microphoneGainPercent);
            output[i] = (short)Math.Clamp(left + right, short.MinValue, short.MaxValue);
        }
    }

    private static int Scale(short sample, int gainPercent) => checked((int)((long)sample * gainPercent / 100));
}

public sealed record RecordingAudioLevel(double Peak, double Rms);

public static class RecordingAudioLevels
{
    public static RecordingAudioLevel Measure(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return new RecordingAudioLevel(0, 0);
        double max = 0;
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            var normalized = sample / 32768.0;
            var absolute = Math.Abs(normalized);
            if (absolute > max) max = absolute;
            sumSquares += normalized * normalized;
        }
        return new RecordingAudioLevel(Math.Clamp(max, 0, 1), Math.Clamp(Math.Sqrt(sumSquares / samples.Length), 0, 1));
    }
}

public static class RecordingAudioTimeline
{
    public static int MissingFrames(long expectedQpc100ns, long actualQpc100ns, TimeSpan tolerance)
    {
        if (expectedQpc100ns < 0) throw new ArgumentOutOfRangeException(nameof(expectedQpc100ns));
        if (actualQpc100ns < 0) throw new ArgumentOutOfRangeException(nameof(actualQpc100ns));
        if (tolerance < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var delta = actualQpc100ns - expectedQpc100ns;
        if (delta <= tolerance.Ticks) return 0;
        var frames = checked(delta * RecordingAudioPolicy.SampleRate / TimeSpan.TicksPerSecond);
        return frames > int.MaxValue ? int.MaxValue : (int)frames;
    }
}
