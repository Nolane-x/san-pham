using Magic.Capture.App.Persistence;
using Magic.Capture.Core.VideoEditing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magic.Capture.App.VideoEditing;

internal sealed record VideoEditTranscodeProgress(double Percent, string Phase);

internal sealed class VideoEditTranscodeService
{
    private readonly VideoEditCompositionService _composition;
    private readonly LocalLog _log;

    public VideoEditTranscodeService(VideoEditCompositionService composition, LocalLog log)
    {
        _composition = composition;
        _log = log;
    }

    public Task ExtractAudioAsync(
        VideoEditProject project,
        string finalPath,
        VideoEditAudioFormat format,
        IProgress<VideoEditTranscodeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        VideoEditExportPolicy.ValidateOutputPath(finalPath, format);
        return TranscodeAsync(project, finalPath, CreateAudioProfile(format), "Extracting audio", progress, cancellationToken);
    }

    public Task ConvertVideoAsync(
        VideoEditProject project,
        string finalPath,
        VideoEditVideoFormat format,
        IProgress<VideoEditTranscodeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        VideoEditExportPolicy.ValidateOutputPath(finalPath, format);
        var profile = CreateVideoProfile(format, project.OutputWidth, project.OutputHeight);
        return TranscodeAsync(project, finalPath, profile, $"Converting {format}", progress, cancellationToken);
    }

    private async Task TranscodeAsync(
        VideoEditProject project,
        string finalPath,
        MediaEncodingProfile profile,
        string phase,
        IProgress<VideoEditTranscodeProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!VideoEditProjectSchema.CanWrite(project.SchemaVersion))
            throw new InvalidOperationException("Future-schema clip projects are read-only and cannot be transcoded.");
        var errors = VideoEditRules.ValidateProject(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));

        var directory = Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException("Transcode output has no parent directory.");
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(finalPath);
        var partialPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.partial{extension}");
        StorageFile? partialFile = null;
        IRandomAccessStream? stream = null;
        try
        {
            progress?.Report(new VideoEditTranscodeProgress(0, "Building composition"));
            var composition = await _composition.BuildCompositionAsync(project, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var source = composition.GenerateMediaStreamSource();

            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            partialFile = await folder.CreateFileAsync(Path.GetFileName(partialPath), CreationCollisionOption.ReplaceExisting);
            stream = await partialFile.OpenAsync(FileAccessMode.ReadWrite);
            stream.Size = 0;

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
            cancellationToken.ThrowIfCancellationRequested();
            if (!prepared.CanTranscode)
                throw new InvalidOperationException($"Windows MediaTranscoder cannot perform this conversion ({prepared.FailureReason}). The requested codec may be unavailable on this system.");

            var operation = prepared.TranscodeAsync();
            operation.Progress = (_, value) => progress?.Report(new VideoEditTranscodeProgress(Math.Clamp(value, 0, 100), phase));
            using var cancellationRegistration = cancellationToken.Register(operation.Cancel);
            await operation;
            cancellationToken.ThrowIfCancellationRequested();
            await stream.FlushAsync();
            if (stream.Size == 0) throw new InvalidDataException("Transcode produced an empty output file.");

            stream.Dispose();
            stream = null;
            progress?.Report(new VideoEditTranscodeProgress(100, "Finalizing"));
            File.Move(partialPath, finalPath, overwrite: true);
            partialFile = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("VideoEdit.Transcode", ex);
            throw;
        }
        finally
        {
            stream?.Dispose();
            if (partialFile is not null || File.Exists(partialPath)) DeletePartialBestEffort(partialPath);
        }
    }

    private static MediaEncodingProfile CreateAudioProfile(VideoEditAudioFormat format) => format switch
    {
        VideoEditAudioFormat.Wav => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
        VideoEditAudioFormat.Mp3 => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
        VideoEditAudioFormat.M4a => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static MediaEncodingProfile CreateVideoProfile(VideoEditVideoFormat format, int width, int height)
    {
        var profile = format switch
        {
            VideoEditVideoFormat.H264Mp4 => MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto),
            VideoEditVideoFormat.HevcMp4 => MediaEncodingProfile.CreateHevc(VideoEncodingQuality.Auto),
            VideoEditVideoFormat.Wmv => MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Auto),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        if (profile.Video is { } video)
        {
            video.Width = checked((uint)VideoEditRules.NormalizeOutputDimension(width));
            video.Height = checked((uint)VideoEditRules.NormalizeOutputDimension(height));
            video.PixelAspectRatio.Numerator = 1;
            video.PixelAspectRatio.Denominator = 1;
        }
        return profile;
    }

    private static void DeletePartialBestEffort(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
