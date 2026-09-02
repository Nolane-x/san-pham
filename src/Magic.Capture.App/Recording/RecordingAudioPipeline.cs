using System.Runtime.InteropServices;
using Magic.Capture.Core.Recording;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingAudioStatus(
    string SystemSource,
    string MicrophoneSource,
    RecordingAudioLevel SystemLevel,
    RecordingAudioLevel MicrophoneLevel,
    long DroppedBytes);

internal sealed class RecordingAudioPipeline : IAsyncDisposable
{
    private readonly RecordingOptions _options;
    private readonly WasapiRecordingAudioSource? _system;
    private readonly WasapiRecordingAudioSource? _microphone;
    private bool _started;

    private RecordingAudioPipeline(
        RecordingOptions options,
        WasapiRecordingAudioSource? system,
        WasapiRecordingAudioSource? microphone)
    {
        _options = options;
        _system = system;
        _microphone = microphone;
    }

    public bool IsEnabled => _system is not null || _microphone is not null;

    public RecordingAudioStatus Status => new(
        _system?.DisplayName ?? "Off",
        _microphone?.DisplayName ?? "Off",
        _system?.LatestLevel ?? new RecordingAudioLevel(0, 0),
        _microphone?.LatestLevel ?? new RecordingAudioLevel(0, 0),
        checked((_system?.DroppedBytes ?? 0) + (_microphone?.DroppedBytes ?? 0)));

    public static RecordingAudioPipeline Create(RecordingOptions options)
    {
        options = RecordingRules.Normalize(options);
        WasapiRecordingAudioSource? system = null;
        WasapiRecordingAudioSource? microphone = null;
        try
        {
            if (options.IncludeSystemAudio)
                system = WasapiRecordingAudioSource.Create(loopback: true, options.SystemAudioDeviceId);
            if (options.IncludeMicrophone)
                microphone = WasapiRecordingAudioSource.Create(loopback: false, options.MicrophoneDeviceId);
            return new RecordingAudioPipeline(options, system, microphone);
        }
        catch
        {
            if (system is not null) system.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (microphone is not null) microphone.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public async Task StartAndWarmUpAsync(CancellationToken cancellationToken)
    {
        if (_started) throw new InvalidOperationException("Recording audio pipeline is already started.");
        _system?.Start();
        try
        {
            _microphone?.Start();
            _started = true;
            if (IsEnabled) await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
            _system?.SetPaused(false);
            _microphone?.SetPaused(false);
            ThrowIfFailed();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public void SetPaused(bool paused)
    {
        _system?.SetPaused(paused);
        _microphone?.SetPaused(paused);
    }

    public Task<IBuffer?> ReadMixedBlockAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailed();
        if (!IsEnabled) return Task.FromResult<IBuffer?>(null);

        var systemBytes = _system is null ? null : new byte[RecordingAudioPolicy.BytesPerBlock];
        var microphoneBytes = _microphone is null ? null : new byte[RecordingAudioPolicy.BytesPerBlock];
        if (systemBytes is not null) _system!.ReadAndFillSilence(systemBytes);
        if (microphoneBytes is not null) _microphone!.ReadAndFillSilence(microphoneBytes);

        var mixed = new byte[RecordingAudioPolicy.BytesPerBlock];
        var outputSamples = MemoryMarshal.Cast<byte, short>(mixed.AsSpan());
        var systemSamples = systemBytes is null
            ? ReadOnlySpan<short>.Empty
            : MemoryMarshal.Cast<byte, short>(systemBytes.AsSpan());
        var microphoneSamples = microphoneBytes is null
            ? ReadOnlySpan<short>.Empty
            : MemoryMarshal.Cast<byte, short>(microphoneBytes.AsSpan());
        RecordingAudioMixer.MixPcm16(
            systemSamples,
            microphoneSamples,
            outputSamples,
            _options.SystemAudioGainPercent,
            _options.MicrophoneGainPercent);
        return Task.FromResult<IBuffer?>(CryptographicBuffer.CreateFromByteArray(mixed));
    }

    public void ThrowIfFailed()
    {
        _system?.ThrowIfFailed();
        _microphone?.ThrowIfFailed();
    }

    public async ValueTask DisposeAsync()
    {
        if (_system is not null) await _system.DisposeAsync();
        if (_microphone is not null) await _microphone.DisposeAsync();
        _started = false;
    }
}
