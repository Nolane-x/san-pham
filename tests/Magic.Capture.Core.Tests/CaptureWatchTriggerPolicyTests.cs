using Magic.Capture.Core.Capture;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureWatchTriggerPolicyTests
{
    [Fact]
    public void ChangedOnlyFirstSampleEstablishesBaselineWithoutTriggering()
    {
        var decision = CaptureWatchTriggerPolicy.Decide(
            onlyWhenChanged: true,
            hasBaseline: false,
            changedPercent: 100,
            minimumChangedPercent: 1);

        Assert.False(decision.ShouldTrigger);
        Assert.True(decision.EstablishBaseline);
    }

    [Fact]
    public void ChangedOnlyTriggersAfterBaselineWhenThresholdIsReached()
    {
        var decision = CaptureWatchTriggerPolicy.Decide(true, true, 12.5, 10);
        Assert.True(decision.ShouldTrigger);
        Assert.False(decision.EstablishBaseline);
    }

    [Fact]
    public void UnconditionalWatchCanTriggerTheFirstSample()
    {
        var decision = CaptureWatchTriggerPolicy.Decide(false, false, 100, 99);
        Assert.True(decision.ShouldTrigger);
    }

    [Fact]
    public void ThresholdAndChangedPercentAreNormalized()
    {
        Assert.False(CaptureWatchTriggerPolicy.Decide(true, true, double.NaN, 10).ShouldTrigger);
        Assert.True(CaptureWatchTriggerPolicy.Decide(true, true, 1000, 1000).ShouldTrigger);
    }
}
