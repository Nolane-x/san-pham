using Magic.Capture.Core.Recording;

namespace Magic.Capture.Core.Tests;

public sealed class RecordingPolicyTests
{
    [Fact]
    public void Normalize_ClampsAllBoundedOptions()
    {
        var normalized = RecordingRules.Normalize(new RecordingOptions(
            FramesPerSecond: 120,
            BitrateMbps: 99,
            ScalePercent: 3,
            IncludeCursor: true,
            CountdownSeconds: 99,
            StopAfterMinutes: 999));

        Assert.Equal(60, normalized.FramesPerSecond);
        Assert.Equal(50, normalized.BitrateMbps);
        Assert.Equal(25, normalized.ScalePercent);
        Assert.Equal(10, normalized.CountdownSeconds);
        Assert.Equal(240, normalized.StopAfterMinutes);
    }

    [Theory]
    [InlineData(1920, 100, 1920)]
    [InlineData(1919, 100, 1918)]
    [InlineData(1919, 50, 958)]
    [InlineData(5, 25, 2)]
    public void ScaleDimension_IsEvenAndBounded(int source, int percent, int expected)
    {
        Assert.Equal(expected, RecordingRules.ScaleDimension(source, percent));
    }

    [Fact]
    public void Cadence_ProducesMonotonicFrameTimestamps()
    {
        var frameDuration = RecordingCadence.FrameDuration(25);
        Assert.Equal(TimeSpan.FromMilliseconds(40), frameDuration);
        Assert.Equal(TimeSpan.Zero, RecordingCadence.TimestampForFrame(0, 25));
        Assert.Equal(TimeSpan.FromMilliseconds(40), RecordingCadence.TimestampForFrame(1, 25));
        Assert.Equal(TimeSpan.FromMilliseconds(400), RecordingCadence.TimestampForFrame(10, 25));
    }

    [Theory]
    [InlineData(59, 1, false)]
    [InlineData(60, 1, true)]
    [InlineData(120, 2, true)]
    public void StopPolicy_UsesActiveElapsedMinutes(int seconds, int minutes, bool expected)
    {
        Assert.Equal(expected, RecordingStopPolicy.ShouldStop(TimeSpan.FromSeconds(seconds), minutes));
    }

    [Fact]
    public void StateMachine_AllowsOnlyLifecycleTransitions()
    {
        Assert.True(RecordingStateMachine.CanTransition(RecordingSessionState.Preparing, RecordingSessionState.Recording));
        Assert.True(RecordingStateMachine.CanTransition(RecordingSessionState.Recording, RecordingSessionState.Paused));
        Assert.True(RecordingStateMachine.CanTransition(RecordingSessionState.Paused, RecordingSessionState.Recording));
        Assert.True(RecordingStateMachine.CanTransition(RecordingSessionState.Recording, RecordingSessionState.Finalizing));
        Assert.True(RecordingStateMachine.CanTransition(RecordingSessionState.Finalizing, RecordingSessionState.Completed));
        Assert.True(RecordingStateMachine.CanTransition(RecordingSessionState.Recording, RecordingSessionState.Failed));
        Assert.False(RecordingStateMachine.CanTransition(RecordingSessionState.Completed, RecordingSessionState.Recording));
        Assert.False(RecordingStateMachine.CanTransition(RecordingSessionState.Preparing, RecordingSessionState.Completed));
    }
}
