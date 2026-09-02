using Magic.Capture.App.Persistence;
using Magic.Capture.Core.VideoEditing;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.UI;

namespace Magic.Capture.App.VideoEditing;

internal sealed record VideoEditRenderProgress(double Percent, string Phase);

internal sealed class VideoEditCompositionService
{
    public const int MaximumGeneratedOverlayPieces = VideoEditOverlayAnimationPolicy.MaximumAnimatedOverlayPieces;

    private readonly LocalLog _log;
    private readonly VideoEditOverlayAssetStore _overlayAssets;

    public VideoEditCompositionService(LocalLog log, VideoEditOverlayAssetStore overlayAssets)
    {
        _log = log;
        _overlayAssets = overlayAssets;
    }

    public async Task<VideoEditSource> ProbeSourceAsync(string path, string? sourceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Video source path must be fully qualified.", nameof(path));
        cancellationToken.ThrowIfCancellationRequested();

        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        cancellationToken.ThrowIfCancellationRequested();
        var properties = clip.GetVideoEncodingProperties();
        if (clip.OriginalDuration <= TimeSpan.Zero || properties.Width == 0 || properties.Height == 0)
            throw new InvalidDataException("Video source has no usable duration or video dimensions.");
        if (properties.Width > int.MaxValue || properties.Height > int.MaxValue)
            throw new InvalidDataException("Video source dimensions exceed supported bounds.");

        return new VideoEditSource(
            string.IsNullOrWhiteSpace(sourceId) ? Guid.NewGuid().ToString("N") : sourceId,
            Path.GetFullPath(path),
            clip.OriginalDuration,
            checked((int)properties.Width),
            checked((int)properties.Height));
    }

    public async Task<MediaComposition> BuildCompositionAsync(
        VideoEditProject project,
        CancellationToken cancellationToken = default,
        bool includeOverlays = true)
    {
        ValidateWritableProjectShape(project);
        var sourceMap = project.Sources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var composition = new MediaComposition();
        var overlayLayer = new MediaOverlayLayer();
        var generatedOverlayPieces = 0;
        var timelineCursor = TimeSpan.Zero;

        foreach (var segment in project.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.IsTitleCard)
            {
                var title = segment.TitleCard!;
                var card = MediaClip.CreateFromColor(ToWindowsColor(title.BackgroundArgb), title.Duration);
                composition.Clips.Add(card);
                if (includeOverlays)
                {
                    var titleOverlay = new VideoEditOverlay(
                        "title-" + Guid.NewGuid().ToString("N"),
                        VideoEditOverlayKind.Text,
                        timelineCursor,
                        title.Duration,
                        new VideoEditCrop(0.08, 0.26, 0.84, 0.48),
                        Opacity: 1.0,
                        FillArgb: title.ForegroundArgb,
                        StrokeArgb: title.ForegroundArgb,
                        StrokeWidth: 0,
                        Text: title.Text,
                        FontScale: title.FontScale,
                        TextStyle: title.TextStyle);
                    generatedOverlayPieces += await AddRasterOverlaysAsync(overlayLayer, titleOverlay, project, MaximumGeneratedOverlayPieces - generatedOverlayPieces, cancellationToken);
                }
                timelineCursor += title.Duration;
                continue;
            }

            var source = sourceMap[segment.SourceId];
            if (!File.Exists(source.Path)) throw new FileNotFoundException($"Clip source is missing: {source.Path}", source.Path);

            var file = await StorageFile.GetFileFromPathAsync(source.Path);
            var clip = await MediaClip.CreateFromFileAsync(file);
            cancellationToken.ThrowIfCancellationRequested();
            if (clip.OriginalDuration <= TimeSpan.Zero || segment.SourceEnd > clip.OriginalDuration)
                throw new InvalidDataException($"Timeline segment exceeds the current duration of source '{source.Path}'.");

            clip.TrimTimeFromStart = segment.SourceStart;
            clip.TrimTimeFromEnd = clip.OriginalDuration - segment.SourceEnd;
            clip.Volume = VideoEditRules.NormalizeVolume(segment.Volume);

            var videoProperties = clip.GetVideoEncodingProperties();
            var transform = new VideoTransformEffectDefinition
            {
                OutputSize = new Size(project.OutputWidth, project.OutputHeight)
            };
            if (segment.Crop is { } crop)
                transform.CropRectangle = ToPixelCrop(VideoEditRules.NormalizeCrop(crop), videoProperties.Width, videoProperties.Height);
            clip.VideoEffectDefinitions.Add(transform);
            composition.Clips.Add(clip);
            timelineCursor += segment.Duration;
        }

        if (composition.Clips.Count == 0) throw new InvalidDataException("Clip project produced an empty composition.");

        if (includeOverlays)
        {
            foreach (var overlay in project.OverlayItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (overlay.Kind == VideoEditOverlayKind.Redaction)
                {
                    generatedOverlayPieces += AddSolidOverlayPieces(overlayLayer, overlay, project, MaximumGeneratedOverlayPieces - generatedOverlayPieces);
                }
                else
                {
                    generatedOverlayPieces += await AddRasterOverlaysAsync(overlayLayer, overlay, project, MaximumGeneratedOverlayPieces - generatedOverlayPieces, cancellationToken);
                }
                if (generatedOverlayPieces > MaximumGeneratedOverlayPieces)
                    throw new InvalidDataException($"Project exceeds the {MaximumGeneratedOverlayPieces} generated overlay-piece render limit.");
            }
        }

        if (overlayLayer.Overlays.Count > 0) composition.OverlayLayers.Add(overlayLayer);
        return composition;
    }

    public async Task<MediaStreamSource> CreatePreviewSourceAsync(
        VideoEditProject project,
        int previewWidth,
        int previewHeight,
        CancellationToken cancellationToken = default)
    {
        var composition = await BuildCompositionAsync(project, cancellationToken);
        var width = Math.Clamp(previewWidth, 160, 1920);
        var height = Math.Clamp(previewHeight, 90, 1080);
        return composition.GeneratePreviewMediaStreamSource(width, height);
    }

    public async Task RenderMp4Async(
        VideoEditProject project,
        string finalPath,
        IProgress<VideoEditRenderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        if (!Path.IsPathFullyQualified(finalPath)) throw new ArgumentException("Video output path must be fully qualified.", nameof(finalPath));
        if (!finalPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Clip editor render output must use .mp4.", nameof(finalPath));
        ValidateWritableProjectShape(project);

        var directory = Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException("Video output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var partialPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.partial.mp4");
        StorageFile? partialFile = null;
        try
        {
            progress?.Report(new VideoEditRenderProgress(0, "Building composition"));
            var composition = await BuildCompositionAsync(project, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            partialFile = await folder.CreateFileAsync(Path.GetFileName(partialPath), CreationCollisionOption.ReplaceExisting);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
            var video = profile.Video ?? throw new InvalidOperationException("Windows did not provide an MP4/H.264 video profile.");
            video.Subtype = MediaEncodingSubtypes.H264;
            video.Width = checked((uint)project.OutputWidth);
            video.Height = checked((uint)project.OutputHeight);
            video.Bitrate = checked((uint)ChooseBitrate(project.OutputWidth, project.OutputHeight));
            video.PixelAspectRatio.Numerator = 1;
            video.PixelAspectRatio.Denominator = 1;

            var operation = composition.RenderToFileAsync(partialFile, MediaTrimmingPreference.Precise, profile);
            operation.Progress = (_, value) => progress?.Report(new VideoEditRenderProgress(Math.Clamp(value, 0, 100), "Rendering MP4"));
            using var cancellationRegistration = cancellationToken.Register(operation.Cancel);
            var failure = await operation;
            cancellationToken.ThrowIfCancellationRequested();
            if (failure != TranscodeFailureReason.None)
                throw new InvalidOperationException($"Windows MediaComposition render failed ({failure}).");

            var properties = await partialFile.GetBasicPropertiesAsync();
            if (properties.Size == 0) throw new InvalidDataException("Clip editor render produced an empty MP4 file.");

            progress?.Report(new VideoEditRenderProgress(100, "Finalizing"));
            File.Move(partialPath, finalPath, overwrite: true);
            partialFile = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("VideoEdit.Render", ex);
            throw;
        }
        finally
        {
            if (partialFile is not null || File.Exists(partialPath)) DeletePartialBestEffort(partialPath);
        }
    }

    private async Task<int> AddRasterOverlaysAsync(
        MediaOverlayLayer layer,
        VideoEditOverlay overlay,
        VideoEditProject project,
        int remainingBudget,
        CancellationToken cancellationToken)
    {
        var asset = await _overlayAssets.GetOrCreateAsync(overlay, project.OutputWidth, project.OutputHeight, cancellationToken);
        var pieces = VideoEditOverlayAnimationPolicy.BuildPieces(overlay, project.OutputFramesPerSecond, remainingBudget);
        foreach (var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clip = await MediaClip.CreateFromImageFileAsync(asset, piece.Duration);
            cancellationToken.ThrowIfCancellationRequested();
            layer.Overlays.Add(new MediaOverlay(clip)
            {
                Delay = piece.Start,
                Position = ToPixelPosition(piece.Value.Bounds, project.OutputWidth, project.OutputHeight),
                Opacity = piece.Value.Opacity,
                AudioEnabled = false
            });
        }
        return pieces.Count;
    }

    private static int AddSolidOverlayPieces(
        MediaOverlayLayer layer,
        VideoEditOverlay overlay,
        VideoEditProject project,
        int remainingBudget)
    {
        var pieces = VideoEditOverlayAnimationPolicy.BuildPieces(overlay, project.OutputFramesPerSecond, remainingBudget);
        foreach (var piece in pieces)
        {
            var clip = MediaClip.CreateFromColor(ToWindowsColor(overlay.FillArgb), piece.Duration);
            layer.Overlays.Add(new MediaOverlay(clip)
            {
                Delay = piece.Start,
                Position = ToPixelPosition(piece.Value.Bounds, project.OutputWidth, project.OutputHeight),
                Opacity = piece.Value.Opacity,
                AudioEnabled = false
            });
        }
        return pieces.Count;
    }

    private static Rect ToPixelPosition(VideoEditCrop bounds, int outputWidth, int outputHeight)
    {
        var safe = VideoEditRules.NormalizeCrop(bounds);
        var x = Math.Clamp(safe.X * outputWidth, 0, outputWidth - 1.0);
        var y = Math.Clamp(safe.Y * outputHeight, 0, outputHeight - 1.0);
        var width = Math.Clamp(safe.Width * outputWidth, 1.0, outputWidth - x);
        var height = Math.Clamp(safe.Height * outputHeight, 1.0, outputHeight - y);
        return new Rect(x, y, width, height);
    }

    private static Rect ToPixelCrop(VideoEditCrop crop, uint sourceWidth, uint sourceHeight)
    {
        if (sourceWidth == 0 || sourceHeight == 0) throw new InvalidDataException("Crop source dimensions are zero.");
        var x = Math.Clamp((int)Math.Floor(crop.X * sourceWidth), 0, checked((int)sourceWidth - 1));
        var y = Math.Clamp((int)Math.Floor(crop.Y * sourceHeight), 0, checked((int)sourceHeight - 1));
        var right = Math.Clamp((int)Math.Ceiling((crop.X + crop.Width) * sourceWidth), x + 1, checked((int)sourceWidth));
        var bottom = Math.Clamp((int)Math.Ceiling((crop.Y + crop.Height) * sourceHeight), y + 1, checked((int)sourceHeight));
        return new Rect(x, y, right - x, bottom - y);
    }

    private static Color ToWindowsColor(uint argb) => Color.FromArgb(
        unchecked((byte)(argb >> 24)),
        unchecked((byte)(argb >> 16)),
        unchecked((byte)(argb >> 8)),
        unchecked((byte)argb));

    private static int ChooseBitrate(int width, int height)
    {
        var pixels = checked((long)width * height);
        return pixels switch
        {
            <= 1280L * 720 => 6_000_000,
            <= 1920L * 1080 => 12_000_000,
            <= 2560L * 1440 => 20_000_000,
            _ => 35_000_000
        };
    }

    private static void ValidateWritableProjectShape(VideoEditProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!VideoEditProjectSchema.CanWrite(project.SchemaVersion))
            throw new InvalidOperationException("Future clip-project schemas are read-only and cannot be rendered by this version.");
        var errors = VideoEditRules.ValidateProject(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
    }

    private static void DeletePartialBestEffort(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
