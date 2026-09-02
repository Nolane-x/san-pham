using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

/// <summary>
/// Reads an encoded image only after its file length has been validated. Keeping the file open
/// with FileShare.Read prevents a writer from growing/replacing it between the length check and
/// the bounded allocation.
/// </summary>
internal static class ImageFileReader
{
    public static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Image path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        ImageWorkloadLimits.ValidateEncodedLength(stream.Length);
        return await BoundedStreamReader.ReadExactAsync(stream, stream.Length, ImageWorkloadLimits.MaximumEncodedBytes, cancellationToken);
    }
}
