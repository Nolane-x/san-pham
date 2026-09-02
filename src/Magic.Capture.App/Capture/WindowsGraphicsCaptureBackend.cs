using System.Runtime.InteropServices;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed class WindowsGraphicsCaptureBackend : ICaptureBackend, IDisposable
{
    private const int FirstFrameTimeoutMilliseconds = 1500;
    private readonly Direct3D11DeviceHost _deviceHost;

    public WindowsGraphicsCaptureBackend(Direct3D11DeviceHost deviceHost) => _deviceHost = deviceHost;

    public CaptureBackendKind Kind => CaptureBackendKind.WindowsGraphicsCapture;

    public CaptureBackendProbe Probe()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            return new(Kind, false, "Windows Graphics Capture requires Windows 10 1903 or later.");
        try
        {
            return GraphicsCaptureSession.IsSupported()
                ? new(Kind, true)
                : new(Kind, false, "GraphicsCaptureSession.IsSupported returned false.");
        }
        catch (Exception ex)
        {
            return new(Kind, false, $"WGC probe failed: {ex.Message}");
        }
    }

    public async Task<CaptureBackendFrame> CaptureAsync(CaptureBackendRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = Probe();
        if (!probe.IsAvailable)
            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Unsupported, probe.Reason ?? "Windows Graphics Capture is unavailable.");

        GraphicsCaptureItem item;
        try
        {
            item = request.TargetKind == CaptureTargetKind.Window
                ? GraphicsCaptureItemInterop.CreateForWindow(request.WindowHandle)
                : GraphicsCaptureItemInterop.CreateForMonitor(request.MonitorHandle);
        }
        catch (Exception ex)
        {
            throw Wrap(ex, "Could not create a GraphicsCaptureItem for the requested target.");
        }

        var sourceBounds = request.BackendBounds;
        ImageWorkloadLimits.ValidateDimensions(sourceBounds.Width, sourceBounds.Height);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.InvalidFrame, "WGC target reported an empty content size.");

        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _deviceHost.GetWinRtDevice(),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);

        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            session.IsCursorCaptureEnabled = request.IncludeCursor;
        else if (!request.IncludeCursor)
        {
            // Older supported builds may not expose cursor control; refusing avoids silently including a cursor when excluded.
            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Unsupported, "This Windows build cannot disable cursor capture for WGC.");
        }

        var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFrameArrived(Direct3D11CaptureFramePool sender, object _) 
        {
            try
            {
                var frame = sender.TryGetNextFrame();
                if (frame is not null && !completion.TrySetResult(frame)) frame.Dispose();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
        framePool.FrameArrived += OnFrameArrived;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FirstFrameTimeoutMilliseconds);
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        try
        {
            session.StartCapture();
            using var frame = await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var width = frame.ContentSize.Width;
            var height = frame.ContentSize.Height;
            ImageWorkloadLimits.ValidateDimensions(width, height);
            if (width != sourceBounds.Width || height != sourceBounds.Height)
                throw new CaptureBackendException(Kind, CaptureBackendFailureKind.InvalidFrame,
                    $"WGC frame size {width}×{height} did not match physical source bounds {sourceBounds.Width}×{sourceBounds.Height}.");

            using var copied = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
            using var bgra = copied.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? SoftwareBitmap.Copy(copied)
                : SoftwareBitmap.Convert(copied, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var png = await WinRtSoftwareBitmapPngEncoder.EncodeAsync(bgra, cancellationToken).ConfigureAwait(false);
            return new CaptureBackendFrame(png, sourceBounds);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Timeout,
                $"WGC did not deliver its first frame within {FirstFrameTimeoutMilliseconds} ms.", ex);
        }
        catch (CaptureBackendException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = CaptureBackendFailureClassifier.FromException(ex);
            if (failure is CaptureBackendFailureKind.DeviceRemoved or CaptureBackendFailureKind.DeviceReset)
                _deviceHost.Invalidate();
            throw new CaptureBackendException(Kind, failure, "Windows Graphics Capture failed.", ex);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }
    }

    public void Dispose() => _deviceHost.Dispose();

    private CaptureBackendException Wrap(Exception ex, string message) =>
        new(Kind, CaptureBackendFailureClassifier.FromException(ex), message, ex);
}
