using Magic.Capture.Core.VideoEditing;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magic.Capture.App.VideoEditing;

internal sealed class VideoEditThumbnailService
{
    private readonly VideoEditCompositionService _composition;

    public VideoEditThumbnailService(VideoEditCompositionService composition) => _composition = composition;


    internal async Task<VideoEditFramePixels> SampleFrameBgraAsync(
        MediaComposition composition,
        TimeSpan position,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(composition);
        var safePosition = ClampPosition(position, composition.Duration);
        var targetWidth = Math.Clamp(width, 32, 1920);
        var targetHeight = Math.Clamp(height, 18, 1080);
        using var thumbnail = await composition.GetThumbnailAsync(safePosition, targetWidth, targetHeight, VideoFramePrecision.NearestFrame);
        return await DecodeBgraAsync(thumbnail, cancellationToken);
    }

    public async Task ExportFramePngAsync(
        VideoEditProject project,
        TimeSpan position,
        string outputPath,
        int width = 0,
        int height = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Path.IsPathFullyQualified(outputPath)) throw new ArgumentException("Frame output path must be fully qualified.", nameof(outputPath));
        var composition = await _composition.BuildCompositionAsync(project, cancellationToken);
        var safePosition = ClampPosition(position, composition.Duration);
        var targetWidth = width <= 0 ? Math.Clamp(project.OutputWidth, 32, 4096) : Math.Clamp(width, 32, 4096);
        var targetHeight = height <= 0 ? Math.Clamp(project.OutputHeight, 18, 4096) : Math.Clamp(height, 18, 4096);

        using var thumbnail = await composition.GetThumbnailAsync(safePosition, targetWidth, targetHeight, VideoFramePrecision.NearestFrame);
        var pixels = await DecodeBgraAsync(thumbnail, cancellationToken);
        await EncodePngAsync(outputPath, pixels.Bytes, pixels.Width, pixels.Height, cancellationToken);
    }

    public async Task ExportContactSheetPngAsync(
        VideoEditProject project,
        string outputPath,
        int frameCount,
        int cellWidth,
        int cellHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Path.IsPathFullyQualified(outputPath)) throw new ArgumentException("Contact-sheet output path must be fully qualified.", nameof(outputPath));
        var composition = await _composition.BuildCompositionAsync(project, cancellationToken);
        var plan = VideoContactSheetPlan.Create(composition.Duration, frameCount, cellWidth, cellHeight);
        if (plan.RequiredBgraBytes > int.MaxValue) throw new InvalidOperationException("Contact sheet exceeds the managed-array limit.");
        var canvas = GC.AllocateUninitializedArray<byte>(checked((int)plan.RequiredBgraBytes));
        Array.Fill<byte>(canvas, 0);

        for (var index = 0; index < plan.FrameCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var thumbnail = await composition.GetThumbnailAsync(plan.Timestamps[index], plan.CellWidth, plan.CellHeight, VideoFramePrecision.NearestFrame);
            var pixels = await DecodeBgraAsync(thumbnail, cancellationToken);
            BlitCover(pixels, canvas, plan.CanvasWidth, plan.CanvasHeight, index % plan.Columns * plan.CellWidth, index / plan.Columns * plan.CellHeight, plan.CellWidth, plan.CellHeight);
        }

        await EncodePngAsync(outputPath, canvas, plan.CanvasWidth, plan.CanvasHeight, cancellationToken);
    }

    private static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new InvalidDataException("Composition duration is zero.");
        if (position <= TimeSpan.Zero) return TimeSpan.Zero;
        var lastTick = TimeSpan.FromTicks(Math.Max(0, duration.Ticks - 1));
        return position > lastTick ? lastTick : position;
    }

    private static async Task<VideoEditFramePixels> DecodeBgraAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 8192 || decoder.PixelHeight > 8192)
            throw new InvalidDataException("Thumbnail dimensions are outside supported bounds.");
        var provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var bytes = provider.DetachPixelData();
        var expected = checked((long)decoder.PixelWidth * decoder.PixelHeight * 4L);
        if (bytes.LongLength != expected) throw new InvalidDataException("Thumbnail decoder returned an unexpected BGRA buffer length.");
        return new VideoEditFramePixels(bytes, checked((int)decoder.PixelWidth), checked((int)decoder.PixelHeight));
    }

    private static void BlitCover(VideoEditFramePixels source, byte[] destination, int destinationWidth, int destinationHeight, int x, int y, int cellWidth, int cellHeight)
    {
        if (x < 0 || y < 0 || x + cellWidth > destinationWidth || y + cellHeight > destinationHeight)
            throw new ArgumentOutOfRangeException(nameof(x), "Contact-sheet cell exceeds the destination canvas.");

        for (var dy = 0; dy < cellHeight; dy++)
        {
            var sy = Math.Min(source.Height - 1, checked(dy * source.Height / cellHeight));
            for (var dx = 0; dx < cellWidth; dx++)
            {
                var sx = Math.Min(source.Width - 1, checked(dx * source.Width / cellWidth));
                var sourceOffset = checked((sy * source.Width + sx) * 4);
                var destinationOffset = checked(((y + dy) * destinationWidth + x + dx) * 4);
                destination[destinationOffset] = source.Bytes[sourceOffset];
                destination[destinationOffset + 1] = source.Bytes[sourceOffset + 1];
                destination[destinationOffset + 2] = source.Bytes[sourceOffset + 2];
                destination[destinationOffset + 3] = 255;
            }
        }
    }

    private static async Task EncodePngAsync(string outputPath, byte[] bgra, int width, int height, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("PNG output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        stream.Size = 0;
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            checked((uint)width),
            checked((uint)height),
            96,
            96,
            bgra);
        cancellationToken.ThrowIfCancellationRequested();
        await encoder.FlushAsync();
        await stream.FlushAsync();
        if (stream.Size == 0) throw new InvalidDataException("PNG encoder produced an empty image.");
    }

}

internal sealed record VideoEditFramePixels(byte[] Bytes, int Width, int Height);
