using Magic.Capture.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingFrameBuffer(IBuffer Buffer, int Width, int Height);
internal sealed record RecordingFramePixels(byte[] BgraBytes, int Width, int Height);

internal static class RecordingFrameDecoder
{
    public static async Task<RecordingFrameBuffer> DecodeBgra8Async(
        byte[] pngBytes,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken)
    {
        var pixels = await DecodeBgra8PixelsAsync(pngBytes, outputWidth, outputHeight, cancellationToken);
        return new RecordingFrameBuffer(ToBuffer(pixels.BgraBytes), pixels.Width, pixels.Height);
    }

    public static async Task<RecordingFramePixels> DecodeBgra8PixelsAsync(
        byte[] pngBytes,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        cancellationToken.ThrowIfCancellationRequested();
        ImageWorkloadLimits.ValidateEncodedLength(pngBytes.LongLength);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(outputWidth, outputHeight);

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = checked((uint)outputWidth),
            ScaledHeight = checked((uint)outputHeight),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        cancellationToken.ThrowIfCancellationRequested();
        if (bitmap.PixelWidth != outputWidth || bitmap.PixelHeight != outputHeight)
            throw new InvalidDataException("Recording frame decoder returned unexpected dimensions.");

        var byteCount = checked((long)outputWidth * outputHeight * 4L);
        if (byteCount <= 0 || byteCount > int.MaxValue || byteCount > uint.MaxValue)
            throw new InvalidDataException("Recording frame buffer exceeds the supported WinRT IBuffer limit.");
        var buffer = new Windows.Storage.Streams.Buffer((uint)byteCount);
        bitmap.CopyToBuffer(buffer);
        if (buffer.Length != (uint)byteCount)
            throw new InvalidDataException("Recording frame buffer length does not match BGRA8 dimensions.");
        var bytes = new byte[(int)byteCount];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        return new RecordingFramePixels(bytes, outputWidth, outputHeight);
    }

    public static IBuffer ToBuffer(byte[] bgraBytes)
    {
        ArgumentNullException.ThrowIfNull(bgraBytes);
        if (bgraBytes.Length == 0) throw new ArgumentException("BGRA8 buffer is empty.", nameof(bgraBytes));
        using var writer = new DataWriter();
        writer.WriteBytes(bgraBytes);
        return writer.DetachBuffer();
    }
}
