using System.Diagnostics;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed record CaptureRoutingResult(
    CaptureBackendFrame Frame,
    CaptureBackendKind Backend,
    IReadOnlyList<CaptureBackendAttempt> Attempts);

/// <summary>Applies capability probing, target normalization, bounded fallback and output validation.</summary>
internal sealed class CaptureBackendRouter : IDisposable
{
    private readonly MonitorService _monitors;
    private readonly LocalLog? _log;
    private readonly IReadOnlyDictionary<CaptureBackendKind, ICaptureBackend> _backends;

    public CaptureBackendRouter(MonitorService monitors, IEnumerable<ICaptureBackend> backends, LocalLog? log = null)
    {
        _monitors = monitors;
        _log = log;
        _backends = backends.ToDictionary(backend => backend.Kind);
        foreach (var kind in Enum.GetValues<CaptureBackendKind>())
            if (!_backends.ContainsKey(kind)) throw new ArgumentException($"Missing capture backend: {kind}.", nameof(backends));
    }

    public async Task<CaptureRoutingResult> CaptureAsync(
        CaptureBackendRequest request,
        CaptureBackendPreference preference,
        CancellationToken cancellationToken)
    {
        if (request.Bounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(request));
        var normalized = NormalizeRequest(request);
        var probes = _backends.Values.Select(backend => backend.Probe()).ToArray();
        var availability = new CaptureBackendAvailability(
            probes.Any(p => p.Backend == CaptureBackendKind.WindowsGraphicsCapture && p.IsAvailable),
            probes.Any(p => p.Backend == CaptureBackendKind.DesktopDuplication && p.IsAvailable),
            probes.Any(p => p.Backend == CaptureBackendKind.Gdi && p.IsAvailable));
        var candidates = CaptureBackendPolicy.BuildCandidates(normalized.TargetKind, normalized.IncludeCursor, availability, preference);
        if (candidates.Count == 0)
            throw new InvalidOperationException("No capture backend is available for the requested target.");

        var attempts = new List<CaptureBackendAttempt>(candidates.Count);
        Exception? lastFailure = null;
        foreach (var kind in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backend = _backends[kind];
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var frame = await backend.CaptureAsync(normalized, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                var finalFrame = ValidateAndCrop(kind, frame, normalized.Bounds);
                attempts.Add(new CaptureBackendAttempt(kind, true, stopwatch.Elapsed, RecoveryCount: frame.RecoveryCount));
                return new CaptureRoutingResult(finalFrame, kind, attempts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                attempts.Add(new CaptureBackendAttempt(kind, false, stopwatch.Elapsed, CaptureBackendFailureKind.Cancelled, "Capture cancelled."));
                throw;
            }
            catch (CaptureBackendException ex)
            {
                stopwatch.Stop();
                lastFailure = ex;
                attempts.Add(new CaptureBackendAttempt(kind, false, stopwatch.Elapsed, ex.FailureKind, ex.Message, ex.RecoveryCount));
                _log?.Error($"CaptureBackend.{kind}", ex);
                if (!CaptureBackendRecoveryPolicy.ShouldFallback(ex.FailureKind)) throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                lastFailure = ex;
                var failure = CaptureBackendFailureClassifier.FromException(ex);
                attempts.Add(new CaptureBackendAttempt(kind, false, stopwatch.Elapsed, failure, ex.Message));
                _log?.Error($"CaptureBackend.{kind}", ex);
                if (!CaptureBackendRecoveryPolicy.ShouldFallback(failure)) throw;
            }
        }

        throw new AggregateException(
            $"All {attempts.Count} applicable capture backend(s) failed for {normalized.TargetKind}.",
            lastFailure is null ? [] : [lastFailure]);
    }

    private CaptureBackendRequest NormalizeRequest(CaptureBackendRequest request)
    {
        if (request.TargetKind == CaptureTargetKind.Window) return request;
        if (request.TargetKind == CaptureTargetKind.VirtualDesktop || request.TargetKind == CaptureTargetKind.RegionCrossMonitor)
            return request with { SourceBounds = null, MonitorHandle = IntPtr.Zero };

        if (request.MonitorHandle != IntPtr.Zero && request.SourceBounds is not null) return request;
        var monitors = _monitors.ListMonitors();
        var containing = monitors.FirstOrDefault(monitor => Contains(monitor.Bounds, request.Bounds));
        if (containing is null)
            return request with { TargetKind = CaptureTargetKind.RegionCrossMonitor, SourceBounds = null, MonitorHandle = IntPtr.Zero };

        var targetKind = request.TargetKind == CaptureTargetKind.Monitor && request.Bounds == containing.Bounds
            ? CaptureTargetKind.Monitor
            : CaptureTargetKind.RegionSingleMonitor;
        return request with
        {
            TargetKind = targetKind,
            MonitorHandle = containing.Handle,
            SourceBounds = containing.Bounds
        };
    }

    private static CaptureBackendFrame ValidateAndCrop(CaptureBackendKind backend, CaptureBackendFrame frame, PixelRect requestedBounds)
    {
        ImageWorkloadLimits.ValidateEncodedLength(frame.PngBytes.LongLength);
        if (!PngDimensions.TryRead(frame.PngBytes, out var width, out var height) || width != frame.FrameBounds.Width || height != frame.FrameBounds.Height)
            throw new CaptureBackendException(backend, CaptureBackendFailureKind.InvalidFrame,
                "Capture backend returned PNG dimensions that do not match its declared physical frame bounds.");
        if (!Contains(frame.FrameBounds, requestedBounds))
            throw new InvalidDataException("Capture backend frame does not contain the requested physical-pixel region.");
        if (frame.FrameBounds == requestedBounds) return frame;

        var local = new PixelRect(
            requestedBounds.X - frame.FrameBounds.X,
            requestedBounds.Y - frame.FrameBounds.Y,
            requestedBounds.Width,
            requestedBounds.Height);
        var cropped = BitmapCodec.CropPng(frame.PngBytes, local);
        return frame with { PngBytes = cropped, FrameBounds = requestedBounds };
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        !outer.IsEmpty && !inner.IsEmpty &&
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    public void Dispose()
    {
        foreach (var backend in _backends.Values.OfType<IDisposable>().Distinct()) backend.Dispose();
    }
}
