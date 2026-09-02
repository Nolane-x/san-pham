using Magic.Capture.Core.Capture;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureRetryPolicyTests
{
    [Fact]
    public void TransientFailure_RetriesOnlyWithinThreeAttemptBudget()
    {
        Assert.True(CaptureRetryPolicy.ShouldRetry(attemptNumber: 1, transientFailure: true));
        Assert.True(CaptureRetryPolicy.ShouldRetry(attemptNumber: 2, transientFailure: true));
        Assert.False(CaptureRetryPolicy.ShouldRetry(attemptNumber: 3, transientFailure: true));
        Assert.False(CaptureRetryPolicy.ShouldRetry(attemptNumber: 1, transientFailure: false));
        Assert.Equal(3, CaptureRetryPolicy.MaximumAttempts);
    }
}
