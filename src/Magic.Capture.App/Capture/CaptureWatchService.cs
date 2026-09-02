using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed record CaptureWatchOptions(
    TimeSpan Interval,
    double MinimumChangedPercent,
    bool OnlyWhenChanged,
    string? WorkflowId,
    int MaximumCaptures = 1000);

internal sealed record CaptureWatchTick(int Sequence, CaptureAsset Asset, double ChangedPercent, bool Triggered);

internal sealed class CaptureWatchService : IDisposable
{
    private readonly CaptureCoordinator _capture;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public CaptureWatchService(CaptureCoordinator capture) => _capture = capture;
    public bool IsRunning => _loop is { IsCompleted: false };
    public event EventHandler<CaptureWatchTick>? Tick;
    public event EventHandler<string>? Stopped;

    public void Start(CaptureWatchOptions options, Func<CaptureWatchTick, CancellationToken, Task> onTriggered)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CaptureWatchService));
        ArgumentNullException.ThrowIfNull(onTriggered);
        if (IsRunning) throw new InvalidOperationException("Capture Watch is already running.");
        if (_capture.LastRegion is null) throw new InvalidOperationException("Capture a region once before starting Capture Watch.");
        if (options.Interval < TimeSpan.FromSeconds(1) || options.Interval > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(options), "Interval must be between 1 second and 1 hour.");
        if (options.MinimumChangedPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(options), "Change threshold must be between 0 and 100 percent.");
        if (options.MaximumCaptures is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(options), "Maximum captures is outside the allowed range.");

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(options, onTriggered, _cts.Token));
    }

    private async Task RunAsync(CaptureWatchOptions options, Func<CaptureWatchTick, CancellationToken, Task> onTriggered, CancellationToken cancellationToken)
    {
        byte[]? previousPixels = null;
        var sequence = 0;
        var triggeredCount = 0;
        var reason = "Stopped.";
        try
        {
            using var timer = new PeriodicTimer(options.Interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                sequence++;
                var asset = _capture.CaptureLastRegion(includeCursor: false);
                using var bitmap = BitmapCodec.Decode(asset.PngBytes);
                var currentPixels = BitmapPixelBuffer.ReadBgra(bitmap);
                var hasBaseline = previousPixels is not null;
                var changed = hasBaseline
                    ? FrameDifference.SampledChangedPercent(previousPixels!, currentPixels, sampleEveryPixels: 2, channelThreshold: 8)
                    : 0d;
                var decision = CaptureWatchTriggerPolicy.Decide(options.OnlyWhenChanged, hasBaseline, changed, options.MinimumChangedPercent);
                var trigger = decision.ShouldTrigger;
                var tick = new CaptureWatchTick(sequence, asset, changed, trigger);
                Tick?.Invoke(this, tick);
                if (trigger)
                {
                    triggeredCount++;
                    await onTriggered(tick, cancellationToken);
                    if (triggeredCount >= options.MaximumCaptures)
                    {
                        reason = $"Reached capture limit ({options.MaximumCaptures}).";
                        break;
                    }
                }
                previousPixels = currentPixels;
            }
        }
        catch (OperationCanceledException) { reason = "Stopped by user."; }
        catch (Exception ex) { reason = ex.Message; }
        finally
        {
            var completedCts = Interlocked.Exchange(ref _cts, null);
            completedCts?.Dispose();
            _loop = null;
            if (!_disposed) Stopped?.Invoke(this, reason);
        }
    }

    public void Stop()
    {
        if (_disposed) return;
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null) return;
        try { cts.Cancel(); }
        finally { cts.Dispose(); }
    }
}
