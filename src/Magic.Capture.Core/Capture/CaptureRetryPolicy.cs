namespace Magic.Capture.Core.Capture;

public static class CaptureRetryPolicy
{
    public const int MaximumAttempts = 3;
    public const int RetryDelayMilliseconds = 40;

    public static bool ShouldRetry(int attemptNumber, bool transientFailure) =>
        transientFailure && attemptNumber >= 1 && attemptNumber < MaximumAttempts;
}
