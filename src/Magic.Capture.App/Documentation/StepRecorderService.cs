using System.Threading.Channels;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Documentation;

internal sealed record DocumentationStepAsset(DocumentationStep Step, byte[] PngBytes);

internal sealed class StepRecorderService : IAsyncDisposable
{
    private readonly MonitorService _monitors;
    private readonly WindowCaptureService _windows;
    private readonly UiAutomationSnapshotService _uiAutomation;
    private readonly ScreenCaptureService _screenCapture;
    private readonly LocalLog _log;
    private readonly object _gate = new();
    private StepRecorderInputTracker? _tracker;
    private Channel<StepRecorderInputAction>? _channel;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private int _sequence;

    public StepRecorderService(
        MonitorService monitors,
        WindowCaptureService windows,
        UiAutomationSnapshotService uiAutomation,
        ScreenCaptureService screenCapture,
        LocalLog log)
    {
        _monitors = monitors;
        _windows = windows;
        _uiAutomation = uiAutomation;
        _screenCapture = screenCapture;
        _log = log;
    }

    public event EventHandler<DocumentationStepAsset>? StepCaptured;
    public event EventHandler<string>? CaptureFailed;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _tracker is not null;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_tracker is not null) return;
            _sequence = 0;
            _cts = new CancellationTokenSource();
            _channel = Channel.CreateBounded<StepRecorderInputAction>(new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
            var tracker = new StepRecorderInputTracker();
            tracker.ActionCaptured += Tracker_ActionCaptured;
            try
            {
                tracker.Start();
                _tracker = tracker;
                _worker = Task.Run(() => RunWorkerAsync(_channel.Reader, _cts.Token));
            }
            catch
            {
                tracker.ActionCaptured -= Tracker_ActionCaptured;
                tracker.Dispose();
                _channel = null;
                _cts.Dispose();
                _cts = null;
                throw;
            }
        }
    }

    public async Task StopAsync()
    {
        StepRecorderInputTracker? tracker;
        Channel<StepRecorderInputAction>? channel;
        CancellationTokenSource? cts;
        Task? worker;
        lock (_gate)
        {
            tracker = _tracker;
            channel = _channel;
            cts = _cts;
            worker = _worker;
            _tracker = null;
            _channel = null;
            _cts = null;
            _worker = null;
        }

        if (tracker is not null)
        {
            tracker.ActionCaptured -= Tracker_ActionCaptured;
            tracker.Dispose();
        }
        channel?.Writer.TryComplete();
        cts?.Cancel();
        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cts?.Dispose();
    }

    private void Tracker_ActionCaptured(object? sender, StepRecorderInputAction action)
    {
        Channel<StepRecorderInputAction>? channel;
        lock (_gate) channel = _channel;
        channel?.Writer.TryWrite(action);
    }

    private async Task RunWorkerAsync(ChannelReader<StepRecorderInputAction> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var action in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await CaptureActionAsync(action, cancellationToken).ConfigureAwait(false);
                    if (result is not null) StepCaptured?.Invoke(this, result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _log.Error("StepRecorderCapture", ex);
                    CaptureFailed?.Invoke(this, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<DocumentationStepAsset?> CaptureActionAsync(StepRecorderInputAction action, CancellationToken cancellationToken)
    {
        var monitor = _monitors.ListMonitors().FirstOrDefault(item => item.Bounds.Contains(action.DesktopPoint));
        if (monitor is null) return null;

        var windows = _windows.ListCapturableWindows()
            .Where(window => !window.Bounds.Intersect(monitor.Bounds).IsEmpty)
            .ToArray();
        var snapshot = await _uiAutomation.CaptureForMonitorAsync(monitor.Bounds, windows).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var cursorNode = UiAutomationSnapshotRules.FindSnapTarget(snapshot, action.DesktopPoint);
        var node = action.SafeKeyGesture is not null
            ? snapshot.Nodes.FirstOrDefault(item => item.HasKeyboardFocus == true) ?? cursorNode
            : cursorNode;
        var target = node is null ? null : ToEvidence(node);

        // Safe shortcut labels never contain typed text. When UIA exposes the focused control,
        // suppress the shortcut entirely for password controls and center the capture on focus.
        if (action.SafeKeyGesture is not null && target?.IsPassword == true) return null;
        var capturePoint = action.DesktopPoint;
        if (action.SafeKeyGesture is not null && node?.HasKeyboardFocus == true && !node.DesktopBounds.IsEmpty)
        {
            capturePoint = new PixelPoint(
                node.DesktopBounds.X + (node.DesktopBounds.Width / 2),
                node.DesktopBounds.Y + (node.DesktopBounds.Height / 2));
            if (!monitor.Bounds.Contains(capturePoint)) capturePoint = action.DesktopPoint;
        }

        var plan = DocumentationPolicy.PlanCapture(monitor.Bounds, capturePoint, target);
        cancellationToken.ThrowIfCancellationRequested();
        var asset = _screenCapture.Capture(
            plan.Bounds,
            CaptureSourceKind.Region,
            "Step Recorder",
            includeCursor: false,
            windowTitle: target?.WindowTitle,
            processName: target?.ProcessName,
            monitorName: monitor.DeviceName);

        var id = Guid.NewGuid().ToString("N");
        var sequence = Interlocked.Increment(ref _sequence);
        var safeGesture = DocumentationPolicy.IsSafeKeyboardGesture(action.SafeKeyGesture) ? action.SafeKeyGesture : null;
        var description = safeGesture is null
            ? DocumentationPolicy.GenerateDescription(target)
            : $"Press {safeGesture}.";
        var title = safeGesture is not null
            ? safeGesture
            : !string.IsNullOrWhiteSpace(target?.Name)
                ? target!.Name
                : $"Step {sequence}";

        var step = DocumentationPolicy.NormalizeStep(new DocumentationStep(
            id,
            action.TimestampUtc,
            $"steps/{id}.png",
            asset.Width,
            asset.Height,
            target,
            action.IsClick ? plan.LocalClick : null,
            action.MouseButton,
            safeGesture,
            title,
            description,
            null));
        return new DocumentationStepAsset(step, asset.PngBytes);
    }

    private static DocumentationTargetEvidence ToEvidence(UiAutomationSnapshotNode node) => new(
        node.StableKey,
        node.ControlType,
        node.Name,
        node.AutomationId,
        node.ProcessName,
        node.WindowTitle,
        node.ProcessId,
        node.DesktopBounds,
        node.IsPassword == true);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        StepCaptured = null;
        CaptureFailed = null;
    }
}
