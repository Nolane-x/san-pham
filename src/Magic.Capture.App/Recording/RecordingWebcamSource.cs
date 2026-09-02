using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Recording;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingWebcamFrame(byte[] BgraBytes, int Width, int Height, DateTimeOffset CapturedUtc);
internal sealed record RecordingWebcamStatus(bool Active, int Width, int Height, DateTimeOffset? LastFrameUtc, string? Failure);

internal sealed class RecordingWebcamSource : IAsyncDisposable
{
    private static readonly TimeSpan MaximumFrameAge = TimeSpan.FromSeconds(2);
    private readonly object _sync = new();
    private readonly TaskCompletionSource<bool> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private RecordingWebcamFrame? _latest;
    private Exception? _failure;
    private bool _disposed;

    public Exception? Failure
    {
        get { lock (_sync) return _failure; }
    }

    public RecordingWebcamStatus Status
    {
        get
        {
            lock (_sync)
            {
                return new RecordingWebcamStatus(
                    _reader is not null && _failure is null,
                    _latest?.Width ?? 0,
                    _latest?.Height ?? 0,
                    _latest?.CapturedUtc,
                    _failure?.Message);
            }
        }
    }

    public async Task StartAsync(string? deviceId, CancellationToken cancellationToken = default)
    {
        if (_capture is not null) throw new InvalidOperationException("Webcam source is already started.");
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly
        };
        if (!string.IsNullOrWhiteSpace(deviceId)) settings.VideoDeviceId = deviceId;

        var capture = new MediaCapture();
        capture.Failed += Capture_Failed;
        _capture = capture;
        try
        {
            await capture.InitializeAsync(settings);
            cancellationToken.ThrowIfCancellationRequested();

            var source = capture.FrameSources.Values
                .Where(item => item.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderByDescending(item => item.CurrentFormat?.VideoFormat?.Width ?? 0)
                .ThenByDescending(item => item.CurrentFormat?.VideoFormat?.Height ?? 0)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("The selected camera does not expose a usable color frame source.");

            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            cancellationToken.ThrowIfCancellationRequested();
            reader.FrameArrived += Reader_FrameArrived;
            _reader = reader;
            var startStatus = await reader.StartAsync();
            if (startStatus != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException($"The selected camera frame reader could not start ({startStatus}).");

            await _firstFrame.Task.WaitAsync(RecordingWebcamPolicy.WarmUpTimeout, cancellationToken);
            ThrowIfFailed();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public RecordingWebcamFrame GetLatestFrame()
    {
        ThrowIfFailed();
        RecordingWebcamFrame? frame;
        lock (_sync) frame = _latest;
        if (frame is null) throw new InvalidOperationException("The webcam has not produced a frame yet.");
        if (DateTimeOffset.UtcNow - frame.CapturedUtc > MaximumFrameAge)
            throw new TimeoutException("The webcam stopped delivering fresh frames.");
        return frame;
    }

    public void ThrowIfFailed()
    {
        Exception? failure;
        lock (_sync) failure = _failure;
        if (failure is not null) throw new InvalidOperationException("Webcam capture failed.", failure);
    }

    private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (_disposed) return;
        try
        {
            using var reference = sender.TryAcquireLatestFrame();
            using var sourceBitmap = reference?.VideoMediaFrame?.SoftwareBitmap;
            if (sourceBitmap is null) return;

            using var owned = sourceBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
                              sourceBitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied
                ? SoftwareBitmap.Copy(sourceBitmap)
                : SoftwareBitmap.Convert(sourceBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            ImageWorkloadLimits.ValidatePixelProcessingDimensions(owned.PixelWidth, owned.PixelHeight);
            var byteCount = checked((long)owned.PixelWidth * owned.PixelHeight * 4L);
            if (byteCount <= 0 || byteCount > int.MaxValue || byteCount > uint.MaxValue)
                throw new InvalidDataException("Webcam BGRA frame exceeds the supported buffer size.");

            var buffer = new Windows.Storage.Streams.Buffer((uint)byteCount);
            owned.CopyToBuffer(buffer);
            if (buffer.Length != (uint)byteCount)
                throw new InvalidDataException("Webcam frame buffer length does not match BGRA dimensions.");
            var bytes = new byte[(int)byteCount];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);

            var frame = new RecordingWebcamFrame(bytes, owned.PixelWidth, owned.PixelHeight, DateTimeOffset.UtcNow);
            lock (_sync) _latest = frame;
            _firstFrame.TrySetResult(true);
        }
        catch (Exception ex)
        {
            SetFailure(ex);
        }
    }

    private void Capture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs) =>
        SetFailure(new InvalidOperationException($"Camera device failure ({errorEventArgs.Code}): {errorEventArgs.Message}"));

    private void SetFailure(Exception failure)
    {
        lock (_sync)
        {
            if (_failure is null)
            {
                _failure = failure;
                _latest = null;
            }
        }
        _firstFrame.TrySetException(failure);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var reader = _reader;
        _reader = null;
        if (reader is not null)
        {
            reader.FrameArrived -= Reader_FrameArrived;
            try { await reader.StopAsync(); }
            catch (Exception stopError)
            {
                lock (_sync) _failure ??= stopError;
            }
            reader.Dispose();
        }

        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.Failed -= Capture_Failed;
            capture.Dispose();
        }
        lock (_sync) _latest = null;
    }
}
