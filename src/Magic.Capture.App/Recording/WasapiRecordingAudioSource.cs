using System.Runtime.InteropServices;
using Magic.Capture.Core.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Magic.Capture.App.Recording;

internal sealed class WasapiRecordingAudioSource : IAsyncDisposable
{
    private static readonly WaveFormat CanonicalFormat = new(
        RecordingAudioPolicy.SampleRate,
        RecordingAudioPolicy.BitsPerSample,
        RecordingAudioPolicy.Channels);

    private readonly object _gate = new();
    private readonly MMDevice _device;
    private readonly WasapiRecorder _recorder;
    private readonly BoundedPcmBuffer _buffer = new(RecordingAudioPolicy.MaximumBufferedBytes);
    private bool _paused;
    private bool _stopRequested;
    private Exception? _failure;
    private RecordingAudioLevel _latestLevel = new(0, 0);

    private WasapiRecordingAudioSource(MMDevice device, WasapiRecorder recorder, string displayName, bool loopback)
    {
        _device = device;
        _recorder = recorder;
        DisplayName = displayName;
        IsLoopback = loopback;
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += OnRecordingStopped;
    }

    public string DisplayName { get; }
    public bool IsLoopback { get; }
    public long DroppedBytes => _buffer.DroppedBytes;
    public RecordingAudioLevel LatestLevel { get { lock (_gate) return _latestLevel; } }

    public static WasapiRecordingAudioSource Create(bool loopback, string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        var expectedFlow = loopback ? DataFlow.Render : DataFlow.Capture;
        MMDevice device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(expectedFlow, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        try
        {
            if (device.State != DeviceState.Active)
                throw new InvalidOperationException($"Audio device '{device.FriendlyName}' is not active.");
            if (device.DataFlow != expectedFlow)
                throw new InvalidOperationException($"Audio device '{device.FriendlyName}' has the wrong endpoint direction.");

            var builder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithBufferLength(50)
                .WithFormat(CanonicalFormat)
                .WithMmcssThreadPriority("Audio");
            if (loopback) builder = builder.WithLoopbackCapture();
            var recorder = builder.Build();
            return new WasapiRecordingAudioSource(device, recorder, device.FriendlyName, loopback);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            _failure = null;
            _stopRequested = false;
            _paused = false;
            _latestLevel = new RecordingAudioLevel(0, 0);
        }
        _buffer.Clear();
        _recorder.StartRecording();
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
        }
        _buffer.Clear();
    }

    public int ReadAndFillSilence(Span<byte> destination)
    {
        ThrowIfFailed();
        return _buffer.ReadAndFillSilence(destination);
    }

    public void ThrowIfFailed()
    {
        Exception? failure;
        lock (_gate) failure = _failure;
        if (failure is not null)
            throw new InvalidOperationException($"Requested {(IsLoopback ? "system audio" : "microphone")} source '{DisplayName}' stopped unexpectedly.", failure);
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (buffer.IsEmpty) return;
        bool paused;
        lock (_gate) paused = _paused;
        if (paused) return;

        // Do not derive silence insertion from QPC/device positions here. Some shared-mode drivers
        // report those positions inconsistently. The recording master clock requests a fixed 20 ms
        // block cadence and BoundedPcmBuffer fills any unavailable bytes with zero instead.
        _ = devicePosition;
        _ = qpcPosition;
        var stable = (flags & AudioClientBufferFlags.Silent) != 0
            ? new byte[buffer.Length]
            : buffer.ToArray();
        _buffer.Write(stable);
        var samples = MemoryMarshal.Cast<byte, short>(stable.AsSpan(0, stable.Length - stable.Length % 2));
        var level = RecordingAudioLevels.Measure(samples);
        lock (_gate) _latestLevel = level;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate)
        {
            if (_stopRequested) return;
            _failure = e.Exception ?? new InvalidOperationException("The Windows audio endpoint stopped without a stop request.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) _stopRequested = true;
        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.RecordingStopped -= OnRecordingStopped;
        try { _recorder.StopRecording(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        await _recorder.DisposeAsync();
        _device.Dispose();
    }
}
