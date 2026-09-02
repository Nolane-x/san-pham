using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Platform;

internal static class ClipboardImageReader
{
    private const long MaximumClipboardImageBytes = 128L * 1024 * 1024;
    public static async Task<byte[]?> ReadPngAsync(CancellationToken cancellationToken = default)
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap)) return null;
        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        if (stream.Size == 0) return null;
        ImageWorkloadLimits.ValidateEncodedLength((long)stream.Size);
        if ((long)stream.Size > MaximumClipboardImageBytes) throw new InvalidDataException("Clipboard image exceeds the 128 MB safe import limit.");
        if (stream.Size > uint.MaxValue) throw new InvalidDataException("Clipboard image is too large.");
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)stream.Size);
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded != (uint)stream.Size) throw new EndOfStreamException("Clipboard image stream ended early.");
        var bytes = new byte[(int)loaded];
        reader.ReadBytes(bytes);
        using var bitmap = BitmapCodec.DecodeForPixelProcessing(bytes);
        ImageWorkloadLimits.ValidateDimensions(bitmap.Width, bitmap.Height);
        return BitmapCodec.EncodePng(bitmap);
    }
}
