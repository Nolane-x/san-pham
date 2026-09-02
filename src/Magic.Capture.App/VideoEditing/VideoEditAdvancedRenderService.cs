using System.Runtime.InteropServices;
using Magic.Capture.App.Persistence;
using Magic.Capture.App.Recording;
using Magic.Capture.Core.Recording;
using Magic.Capture.Core.VideoEditing;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magic.Capture.App.VideoEditing;

internal sealed class VideoEditAdvancedRenderService
{
    private readonly VideoEditCompositionService _composition;
    private readonly VideoEditThumbnailService _thumbnails;
    private readonly LocalLog _log;

    public VideoEditAdvancedRenderService(
        VideoEditCompositionService composition,
        VideoEditThumbnailService thumbnails,
        LocalLog log)
    {
        _composition = composition;
        _thumbnails = thumbnails;
        _log = log;
    }

    public async Task RenderMp4Async(
        VideoEditProject project,
        string finalPath,
        IProgress<VideoEditRenderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        if (!Path.IsPathFullyQualified(finalPath) || !finalPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Advanced editor render output must be a fully-qualified .mp4 path.", nameof(finalPath));
        if (!VideoEditFrameEffectPolicy.RequiresAdvancedRender(project))
        {
            await _composition.RenderMp4Async(project, finalPath, progress, cancellationToken);
            return;
        }
        if (!VideoEditProjectSchema.CanWrite(project.SchemaVersion))
            throw new InvalidOperationException("Future-schema clip projects are read-only and cannot be rendered.");
        var errors = VideoEditRules.ValidateProject(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));

        var directory = Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException("Render output has no parent directory.");
        Directory.CreateDirectory(directory);
        var token = Guid.NewGuid().ToString("N");
        var partialPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(finalPath)}.{token}.partial.mp4");
        var stagedWavPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(finalPath)}.{token}.base-audio.wav");
        StorageFile? partialFile = null;
        VideoEditPcmWavReader? audioReader = null;
        try
        {
            progress?.Report(new VideoEditRenderProgress(0, "Building advanced timeline"));
            var baseProject = RemapOverlaysForBaseTimeline(project);
            var visualComposition = await _composition.BuildCompositionAsync(baseProject, cancellationToken);
            var audioBaseProject = NormalizeSegmentsToBaseTimeline(project) with
            {
                Overlays = Array.Empty<VideoEditOverlay>(),
                FrameEffects = Array.Empty<VideoEditFrameEffect>()
            };
            var audioComposition = await _composition.BuildCompositionAsync(audioBaseProject, cancellationToken, includeOverlays: false);

            var hasAudio = project.Segments.Any(x => !x.IsTitleCard && x.Volume > 0.000001);
            if (hasAudio)
            {
                progress?.Report(new VideoEditRenderProgress(2, "Staging PCM audio"));
                await StagePcmAudioAsync(audioComposition, stagedWavPath, cancellationToken);
                audioReader = new VideoEditPcmWavReader(stagedWavPath);
            }

            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            partialFile = await folder.CreateFileAsync(Path.GetFileName(partialPath), CreationCollisionOption.ReplaceExisting);
            var options = new RecordingOptions(
                FramesPerSecond: project.OutputFramesPerSecond,
                BitrateMbps: ChooseBitrateMbps(project.OutputWidth, project.OutputHeight),
                AudioBitrateKbps: 192,
                OutputFormat: RecordingOutputFormat.Mp4);
            var encoder = new Mp4RecordingEncoder();
            var duration = project.TimelineDuration;
            var totalFrames = Math.Max(1L, (long)Math.Ceiling(duration.TotalSeconds * project.OutputFramesPerSecond));
            var totalBlocks = Math.Max(1L, (long)Math.Ceiling(duration.TotalMilliseconds / RecordingAudioPolicy.BlockMilliseconds));

            await encoder.EncodeAsync(
                partialFile,
                project.OutputWidth,
                project.OutputHeight,
                options,
                async (index, token2) =>
                {
                    if (index >= totalFrames) return null;
                    token2.ThrowIfCancellationRequested();
                    var outputTime = RecordingCadence.TimestampForFrame(index, project.OutputFramesPerSecond);
                    if (outputTime >= duration) return null;
                    var mapped = VideoEditTimelineMap.MapOutputToBaseTimeline(project.Segments, outputTime);
                    var pixels = await _thumbnails.SampleFrameBgraAsync(visualComposition, mapped.BaseTimelinePosition, project.OutputWidth, project.OutputHeight, token2);
                    ApplyFrameEffects(pixels.Bytes, pixels.Width, pixels.Height, project.FrameEffectItems, outputTime);
                    var percent = 5.0 + 90.0 * Math.Clamp(index / (double)totalFrames, 0, 1);
                    progress?.Report(new VideoEditRenderProgress(percent, "Rendering speed/effects"));
                    return CryptographicBuffer.CreateFromByteArray(pixels.Bytes);
                },
                audioReader is null
                    ? null
                    : (index, token2) =>
                    {
                        if (index >= totalBlocks) return Task.FromResult<IBuffer?>(null);
                        var block = audioReader.ReadRetimedBlock(project, index);
                        return Task.FromResult<IBuffer?>(block is null ? null : CryptographicBuffer.CreateFromByteArray(block));
                    },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var properties = await partialFile.GetBasicPropertiesAsync();
            if (properties.Size == 0) throw new InvalidDataException("Advanced render produced an empty MP4 file.");
            audioReader?.Dispose();
            audioReader = null;
            progress?.Report(new VideoEditRenderProgress(100, "Finalizing"));
            File.Move(partialPath, finalPath, overwrite: true);
            partialFile = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("VideoEdit.AdvancedRender", ex);
            throw;
        }
        finally
        {
            audioReader?.Dispose();
            DeleteBestEffort(stagedWavPath);
            if (partialFile is not null || File.Exists(partialPath)) DeleteBestEffort(partialPath);
        }
    }

    private static VideoEditProject NormalizeSegmentsToBaseTimeline(VideoEditProject project)
    {
        var segments = project.Segments.Select(segment => segment with { PlaybackRate = 1.0 }).ToArray();
        return project with { Segments = segments };
    }

    private static VideoEditProject RemapOverlaysForBaseTimeline(VideoEditProject project)
    {
        var baseProject = NormalizeSegmentsToBaseTimeline(project);
        if (project.OverlayItems.Count == 0) return baseProject with { FrameEffects = Array.Empty<VideoEditFrameEffect>() };
        var remapped = new List<VideoEditOverlay>(project.OverlayItems.Count);
        foreach (var overlay in project.OverlayItems)
        {
            var start = VideoEditTimelineMap.MapOutputToBaseTimeline(project.Segments, overlay.Start).BaseTimelinePosition;
            var endOutput = overlay.End <= TimeSpan.Zero ? overlay.Start : TimeSpan.FromTicks(Math.Max(overlay.Start.Ticks, overlay.End.Ticks - 1));
            var end = VideoEditTimelineMap.MapOutputToBaseTimeline(project.Segments, endOutput).BaseTimelinePosition;
            var duration = end > start ? end - start : TimeSpan.FromMilliseconds(50);
            IReadOnlyList<VideoEditOverlayKeyframe>? keyframes = null;
            if (overlay.Keyframes is { Count: > 0 })
            {
                var list = new List<VideoEditOverlayKeyframe>(overlay.Keyframes.Count);
                foreach (var keyframe in overlay.Keyframes)
                {
                    var absoluteOutput = overlay.Start + keyframe.Offset;
                    var absoluteBase = VideoEditTimelineMap.MapOutputToBaseTimeline(project.Segments, absoluteOutput).BaseTimelinePosition;
                    var offset = absoluteBase > start ? absoluteBase - start : TimeSpan.Zero;
                    if (list.Count > 0 && offset <= list[^1].Offset) offset = list[^1].Offset + TimeSpan.FromTicks(1);
                    if (offset <= duration) list.Add(keyframe with { Offset = offset });
                }
                keyframes = list;
            }
            remapped.Add(overlay with { Start = start, Duration = duration, Keyframes = keyframes });
        }
        return baseProject with { Overlays = remapped, FrameEffects = Array.Empty<VideoEditFrameEffect>() };
    }

    private static void ApplyFrameEffects(byte[] pixels, int width, int height, IReadOnlyList<VideoEditFrameEffect> effects, TimeSpan outputTime)
    {
        foreach (var effect in effects)
        {
            if (outputTime < effect.Start || outputTime >= effect.End) continue;
            var value = VideoEditFrameEffectPolicy.Evaluate(effect, outputTime);
            switch (effect.Kind)
            {
                case VideoEditFrameEffectKind.ZoomPan:
                    VideoEditBgraEffects.ApplyZoomPanInPlace(pixels, width, height, value.Primary, value.X, value.Y);
                    break;
                case VideoEditFrameEffectKind.GaussianBlur:
                    VideoEditBgraEffects.ApplyGaussianBlurInPlace(pixels, width, height, checked((int)Math.Round(value.Primary)));
                    break;
                case VideoEditFrameEffectKind.Pixelate:
                    VideoEditBgraEffects.ApplyPixelateInPlace(pixels, width, height, checked((int)Math.Round(value.Primary)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(effect.Kind));
            }
        }
    }

    private static async Task StagePcmAudioAsync(MediaComposition composition, string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Audio staging path has no parent directory.");
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        stream.Size = 0;
        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
        profile.Video = null;
        profile.Audio = AudioEncodingProperties.CreatePcm(
            checked((uint)RecordingAudioPolicy.SampleRate),
            checked((uint)RecordingAudioPolicy.Channels),
            checked((uint)RecordingAudioPolicy.BitsPerSample));
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(composition.GenerateMediaStreamSource(), stream, profile);
        if (!prepared.CanTranscode)
            throw new InvalidOperationException($"Windows MediaTranscoder cannot stage editor PCM audio ({prepared.FailureReason}).");
        var operation = prepared.TranscodeAsync();
        using var registration = cancellationToken.Register(operation.Cancel);
        await operation;
        cancellationToken.ThrowIfCancellationRequested();
        await stream.FlushAsync();
        if (stream.Size <= 44) throw new InvalidDataException("PCM audio staging produced an empty WAV file.");
    }

    private static int ChooseBitrateMbps(int width, int height)
    {
        var pixels = checked((long)width * height);
        return pixels switch
        {
            <= 1280L * 720 => 6,
            <= 1920L * 1080 => 12,
            <= 2560L * 1440 => 20,
            _ => 35
        };
    }

    private static void DeleteBestEffort(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class VideoEditPcmWavReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly long _dataOffset;
    private readonly long _frameCount;
    private readonly byte[] _frameBuffer = new byte[8];

    public VideoEditPcmWavReader(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        (_dataOffset, var dataBytes, var sampleRate, var channels, var bits) = ParseWave(_stream);
        if (sampleRate != RecordingAudioPolicy.SampleRate || channels != RecordingAudioPolicy.Channels || bits != RecordingAudioPolicy.BitsPerSample)
            throw new InvalidDataException("Editor PCM staging format is not 48 kHz stereo PCM16.");
        _frameCount = dataBytes / (RecordingAudioPolicy.Channels * RecordingAudioPolicy.BytesPerSample);
    }

    public byte[]? ReadRetimedBlock(VideoEditProject project, long outputBlockIndex)
    {
        if (outputBlockIndex < 0) throw new ArgumentOutOfRangeException(nameof(outputBlockIndex));
        var start = RecordingAudioPolicy.TimestampForBlock(outputBlockIndex);
        if (start >= project.TimelineDuration) return null;
        var output = new byte[RecordingAudioPolicy.BytesPerBlock];
        var samples = MemoryMarshal.Cast<byte, short>(output.AsSpan());
        for (var frame = 0; frame < RecordingAudioPolicy.FramesPerBlock; frame++)
        {
            var outputTime = start + TimeSpan.FromTicks(checked(frame * TimeSpan.TicksPerSecond / RecordingAudioPolicy.SampleRate));
            if (outputTime >= project.TimelineDuration) break;
            var mapped = VideoEditTimelineMap.MapOutputToBaseTimeline(project.Segments, outputTime);
            var sourceFrame = mapped.BaseTimelinePosition.Ticks * (double)RecordingAudioPolicy.SampleRate / TimeSpan.TicksPerSecond;
            ReadInterpolatedFrame(sourceFrame, out var left, out var right);
            var gain = VideoEditAudioEnvelopePolicy.Evaluate(project.Segments[mapped.SegmentIndex].AudioEnvelope, mapped.OutputOffsetInSegment);
            samples[frame * 2] = ApplyGain(left, gain);
            samples[frame * 2 + 1] = ApplyGain(right, gain);
        }
        return output;
    }

    private static short ApplyGain(short sample, double gain) =>
        (short)Math.Clamp((int)Math.Round(sample * VideoEditAudioEnvelopePolicy.NormalizeGain(gain)), short.MinValue, short.MaxValue);

    private void ReadInterpolatedFrame(double framePosition, out short left, out short right)
    {
        if (_frameCount <= 0) { left = right = 0; return; }
        var lower = Math.Clamp((long)Math.Floor(framePosition), 0, _frameCount - 1);
        var upper = Math.Min(_frameCount - 1, lower + 1);
        var fraction = Math.Clamp(framePosition - lower, 0.0, 1.0);
        ReadFrame(lower, out var l0, out var r0);
        ReadFrame(upper, out var l1, out var r1);
        left = (short)Math.Clamp((int)Math.Round(l0 + (l1 - l0) * fraction), short.MinValue, short.MaxValue);
        right = (short)Math.Clamp((int)Math.Round(r0 + (r1 - r0) * fraction), short.MinValue, short.MaxValue);
    }

    private void ReadFrame(long frame, out short left, out short right)
    {
        var offset = checked(_dataOffset + frame * 4L);
        _stream.Position = offset;
        var read = 0;
        while (read < 4)
        {
            var count = _stream.Read(_frameBuffer, read, 4 - read);
            if (count <= 0) { left = right = 0; return; }
            read += count;
        }
        left = BitConverter.ToInt16(_frameBuffer, 0);
        right = BitConverter.ToInt16(_frameBuffer, 2);
    }

    private static (long DataOffset, long DataBytes, int SampleRate, int Channels, int Bits) ParseWave(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("WAV RIFF header is missing.");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("WAV format header is missing.");
        int sampleRate = 0, channels = 0, bits = 0, formatTag = 0;
        long dataOffset = -1, dataBytes = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var next = checked(stream.Position + size + (size & 1));
            if (next > stream.Length) throw new InvalidDataException("WAV chunk exceeds file length.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("WAV fmt chunk is too small.");
                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = checked((int)reader.ReadUInt32());
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                bits = reader.ReadUInt16();
            }
            else if (id == "data")
            {
                dataOffset = stream.Position;
                dataBytes = size;
            }
            stream.Position = next;
            if (dataOffset >= 0 && sampleRate > 0) break;
        }
        if (formatTag != 1 || dataOffset < 0) throw new InvalidDataException("WAV must contain uncompressed PCM data.");
        return (dataOffset, dataBytes, sampleRate, channels, bits);
    }

    public void Dispose() => _stream.Dispose();
}
