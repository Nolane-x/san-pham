using Magic.Capture.Core.Recording;

namespace Magic.Capture.Core.Tests;

public sealed class RecordingAudioPolicyTests
{
    [Fact]
    public void Normalize_ClampsAudioOptionsAndTrimsDeviceIds()
    {
        var normalized = RecordingRules.Normalize(new RecordingOptions(
            IncludeSystemAudio: true,
            IncludeMicrophone: true,
            SystemAudioDeviceId: "  render-id  ",
            MicrophoneDeviceId: "  mic-id  ",
            AudioBitrateKbps: 999,
            SystemAudioGainPercent: -10,
            MicrophoneGainPercent: 999));

        Assert.Equal("render-id", normalized.SystemAudioDeviceId);
        Assert.Equal("mic-id", normalized.MicrophoneDeviceId);
        Assert.Equal(320, normalized.AudioBitrateKbps);
        Assert.Equal(0, normalized.SystemAudioGainPercent);
        Assert.Equal(200, normalized.MicrophoneGainPercent);
    }

    [Fact]
    public void Cadence_IsTwentyMillisecondsAtCanonicalFormat()
    {
        Assert.Equal(48_000, RecordingAudioPolicy.SampleRate);
        Assert.Equal(2, RecordingAudioPolicy.Channels);
        Assert.Equal(16, RecordingAudioPolicy.BitsPerSample);
        Assert.Equal(TimeSpan.FromMilliseconds(20), RecordingAudioPolicy.BlockDuration);
        Assert.Equal(960, RecordingAudioPolicy.FramesPerBlock);
        Assert.Equal(3_840, RecordingAudioPolicy.BytesPerBlock);
        Assert.Equal(TimeSpan.FromMilliseconds(200), RecordingAudioPolicy.TimestampForBlock(10));
    }

    [Fact]
    public void Mixer_SaturatesAndAppliesIndependentGains()
    {
        short[] system = [20_000, -20_000, 1_000, -1_000];
        short[] microphone = [20_000, -20_000, 1_000, -1_000];
        var output = new short[4];

        RecordingAudioMixer.MixPcm16(system, microphone, output, systemGainPercent: 100, microphoneGainPercent: 100);

        Assert.Equal(short.MaxValue, output[0]);
        Assert.Equal(short.MinValue, output[1]);
        Assert.Equal((short)2_000, output[2]);
        Assert.Equal((short)-2_000, output[3]);
    }

    [Fact]
    public void Mixer_UsesSilenceForMissingSource()
    {
        short[] microphone = [1_000, -2_000];
        var output = new short[2];

        RecordingAudioMixer.MixPcm16(ReadOnlySpan<short>.Empty, microphone, output, 100, 50);

        Assert.Equal((short)500, output[0]);
        Assert.Equal((short)-1_000, output[1]);
    }

    [Fact]
    public void LevelMeter_ComputesPeakAndRmsWithoutNaN()
    {
        short[] samples = [0, 16_384, -16_384, 0];
        var level = RecordingAudioLevels.Measure(samples);

        Assert.InRange(level.Peak, 0.49, 0.51);
        Assert.InRange(level.Rms, 0.34, 0.36);
        Assert.False(double.IsNaN(level.Peak));
        Assert.False(double.IsNaN(level.Rms));
    }

    [Fact]
    public void GapMath_ConvertsQpcGapToCanonicalFramesAndHonorsTolerance()
    {
        const long expectedQpc100ns = 1_000_000;
        const long tenMillisecondsLater = 1_100_000;

        Assert.Equal(480, RecordingAudioTimeline.MissingFrames(expectedQpc100ns, tenMillisecondsLater, TimeSpan.FromMilliseconds(2)));
        Assert.Equal(0, RecordingAudioTimeline.MissingFrames(expectedQpc100ns, expectedQpc100ns + 10_000, TimeSpan.FromMilliseconds(2)));
    }
}
