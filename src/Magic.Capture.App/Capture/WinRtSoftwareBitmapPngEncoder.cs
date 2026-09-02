using Magic.Capture.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Capture;

internal static class WinRtSoftwareBitmapPngEncoder
{
    public static async Task<byte[]> EncodeAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImageWorkloadLimits.ValidateDimensions(bitmap.PixelWidth, bitmap.PixelHeight);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.Size == 0 || stream.Size > (ulong)ImageWorkloadLimits.MaximumEncodedBytes)
            throw new InvalidDataException("Windows Graphics Capture produced an invalid encoded image size.");
        if (stream.Size > uint.MaxValue)
            throw new InvalidDataException("Windows Graphics Capture payload exceeds the WinRT reader limit.");

        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        ImageWorkloadLimits.ValidateEncodedLength(bytes.LongLength);
        return bytes;
    }
}
