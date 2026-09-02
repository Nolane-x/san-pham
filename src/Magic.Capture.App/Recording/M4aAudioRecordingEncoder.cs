using Magic.Capture.Core.Recording;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal sealed class M4aAudioRecordingEncoder
{
    public async Task EncodeAsync(
        StorageFile outputFile,
        RecordingOptions options,
        Func<long, CancellationToken, Task<IBuffer?>> audioFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputFile);
        ArgumentNullException.ThrowIfNull(audioFactory);
        options = RecordingRules.Normalize(options);
        if (!RecordingOutputPolicy.IsAudioOnly(options.OutputFormat))
            throw new ArgumentException("The M4A encoder requires the audio-only recording output format.", nameof(options));

        var audioInput = AudioEncodingProperties.CreatePcm(
            checked((uint)RecordingAudioPolicy.SampleRate),
            checked((uint)RecordingAudioPolicy.Channels),
            checked((uint)RecordingAudioPolicy.BitsPerSample));
        var descriptor = new AudioStreamDescriptor(audioInput);
        var source = new MediaStreamSource(descriptor)
        {
            BufferTime = TimeSpan.Zero,
            CanSeek = false,
            IsLive = true
        };

        long blockIndex = 0;
        Exception? sampleFailure = null;
        var gate = new SemaphoreSlim(1, 1);

        void StartingHandler(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) =>
            args.Request.SetActualStartPosition(TimeSpan.Zero);

        async void SampleHandler(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var index = blockIndex;
                    var buffer = await audioFactory(index, cancellationToken);
                    if (buffer is null)
                    {
                        args.Request.Sample = null;
                        return;
                    }
                    var sample = MediaStreamSample.CreateFromBuffer(buffer, RecordingAudioPolicy.TimestampForBlock(index));
                    sample.Duration = RecordingAudioPolicy.BlockDuration;
                    args.Request.Sample = sample;
                    blockIndex = checked(index + 1);
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException)
            {
                args.Request.Sample = null;
            }
            catch (Exception ex)
            {
                sampleFailure = ex;
                try { sender.NotifyError(MediaStreamSourceErrorStatus.Other); }
                catch (Exception notifyError) { sampleFailure = new AggregateException(ex, notifyError); }
            }
            finally { deferral.Complete(); }
        }

        source.Starting += StartingHandler;
        source.SampleRequested += SampleHandler;
        try
        {
            var profile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
            profile.Video = null;
            profile.Audio = AudioEncodingProperties.CreateAac(
                checked((uint)RecordingAudioPolicy.SampleRate),
                checked((uint)RecordingAudioPolicy.Channels),
                checked((uint)(options.AudioBitrateKbps * 1_000)));
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
            using var outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
            outputStream.Size = 0;
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, outputStream, profile);
            if (!prepared.CanTranscode)
                throw new InvalidOperationException($"Windows Media Transcoder cannot encode M4A/AAC ({prepared.FailureReason}).");
            try { await prepared.TranscodeAsync(); }
            catch (Exception ex) when (sampleFailure is not null)
            {
                throw new InvalidOperationException("Audio-only sample production failed while M4A was encoding.", sampleFailure ?? ex);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await outputStream.FlushAsync();
            if (sampleFailure is not null) throw new InvalidOperationException("Audio-only sample production failed.", sampleFailure);
            if (outputStream.Size == 0) throw new InvalidDataException("Windows Media Transcoder produced an empty M4A file.");
        }
        finally
        {
            source.SampleRequested -= SampleHandler;
            source.Starting -= StartingHandler;
            gate.Dispose();
        }
    }
}
