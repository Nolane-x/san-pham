using Magic.Capture.Core.Recording;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal sealed class Mp4RecordingEncoder
{
    public async Task EncodeAsync(
        StorageFile outputFile,
        int width,
        int height,
        RecordingOptions options,
        Func<long, CancellationToken, Task<IBuffer?>> frameFactory,
        Func<long, CancellationToken, Task<IBuffer?>>? audioFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputFile);
        ArgumentNullException.ThrowIfNull(frameFactory);
        options = RecordingRules.Normalize(options);
        if (width < 2 || height < 2 || (width & 1) != 0 || (height & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(width), "H.264 recording dimensions must be even and at least 2×2.");

        var videoInput = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8,
            checked((uint)width),
            checked((uint)height));
        videoInput.FrameRate.Numerator = checked((uint)options.FramesPerSecond);
        videoInput.FrameRate.Denominator = 1;
        videoInput.PixelAspectRatio.Numerator = 1;
        videoInput.PixelAspectRatio.Denominator = 1;

        var videoDescriptor = new VideoStreamDescriptor(videoInput);
        AudioStreamDescriptor? audioDescriptor = null;
        MediaStreamSource source;
        if (audioFactory is null)
        {
            source = new MediaStreamSource(videoDescriptor);
        }
        else
        {
            var audioInput = AudioEncodingProperties.CreatePcm(
                checked((uint)RecordingAudioPolicy.SampleRate),
                checked((uint)RecordingAudioPolicy.Channels),
                checked((uint)RecordingAudioPolicy.BitsPerSample));
            audioDescriptor = new AudioStreamDescriptor(audioInput);
            source = new MediaStreamSource(videoDescriptor, audioDescriptor);
        }

        source.BufferTime = TimeSpan.Zero;
        source.CanSeek = false;
        source.IsLive = true;

        long videoFrameIndex = 0;
        long audioBlockIndex = 0;
        Exception? sampleFailure = null;
        var videoGate = new SemaphoreSlim(1, 1);
        var audioGate = new SemaphoreSlim(1, 1);
        var frameDuration = RecordingCadence.FrameDuration(options.FramesPerSecond);

        void StartingHandler(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) =>
            args.Request.SetActualStartPosition(TimeSpan.Zero);

        async void SampleHandler(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                if (audioDescriptor is not null && args.Request.StreamDescriptor is AudioStreamDescriptor)
                {
                    await audioGate.WaitAsync(cancellationToken);
                    try
                    {
                        var index = audioBlockIndex;
                        var buffer = await audioFactory!(index, cancellationToken);
                        if (buffer is null)
                        {
                            args.Request.Sample = null;
                            return;
                        }

                        var sample = MediaStreamSample.CreateFromBuffer(buffer, RecordingAudioPolicy.TimestampForBlock(index));
                        sample.Duration = RecordingAudioPolicy.BlockDuration;
                        args.Request.Sample = sample;
                        audioBlockIndex = checked(index + 1);
                    }
                    finally { audioGate.Release(); }
                }
                else
                {
                    await videoGate.WaitAsync(cancellationToken);
                    try
                    {
                        var index = videoFrameIndex;
                        var buffer = await frameFactory(index, cancellationToken);
                        if (buffer is null)
                        {
                            args.Request.Sample = null;
                            return;
                        }

                        var sample = MediaStreamSample.CreateFromBuffer(buffer, RecordingCadence.TimestampForFrame(index, options.FramesPerSecond));
                        sample.Duration = frameDuration;
                        sample.KeyFrame = index == 0;
                        args.Request.Sample = sample;
                        videoFrameIndex = checked(index + 1);
                    }
                    finally { videoGate.Release(); }
                }
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
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
            var video = profile.Video ?? throw new InvalidOperationException("Windows did not provide an H.264 video profile.");
            video.Subtype = MediaEncodingSubtypes.H264;
            video.Width = checked((uint)width);
            video.Height = checked((uint)height);
            video.Bitrate = checked((uint)(options.BitrateMbps * 1_000_000));
            video.FrameRate.Numerator = checked((uint)options.FramesPerSecond);
            video.FrameRate.Denominator = 1;
            video.PixelAspectRatio.Numerator = 1;
            video.PixelAspectRatio.Denominator = 1;

            profile.Audio = audioFactory is null
                ? null
                : AudioEncodingProperties.CreateAac(
                    checked((uint)RecordingAudioPolicy.SampleRate),
                    checked((uint)RecordingAudioPolicy.Channels),
                    checked((uint)(options.AudioBitrateKbps * 1_000)));

            var transcoder = new MediaTranscoder
            {
                HardwareAccelerationEnabled = true,
                AlwaysReencode = true
            };

            using var outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
            outputStream.Size = 0;
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, outputStream, profile);
            if (!prepared.CanTranscode)
                throw new InvalidOperationException($"Windows Media Transcoder cannot encode this recording profile ({prepared.FailureReason}).");

            try
            {
                await prepared.TranscodeAsync();
            }
            catch (Exception ex) when (sampleFailure is not null)
            {
                throw new InvalidOperationException("Recording media sample production failed while the MP4 stream was being encoded.", sampleFailure ?? ex);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await outputStream.FlushAsync();
            if (sampleFailure is not null)
                throw new InvalidOperationException("Recording media sample production failed.", sampleFailure);
            if (outputStream.Size == 0)
                throw new InvalidDataException("Windows Media Transcoder produced an empty MP4 file.");
        }
        finally
        {
            source.SampleRequested -= SampleHandler;
            source.Starting -= StartingHandler;
            videoGate.Dispose();
            audioGate.Dispose();
        }
    }
}
