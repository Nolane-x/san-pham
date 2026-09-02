using Magic.Capture.App.Imaging;
using Magic.Capture.App.Utilities;
using Magic.Capture.Core.Export;

namespace Magic.Capture.App.Export;

internal sealed class PdfExportService
{
    private readonly ImageUtilityService _imageUtilities;

    public PdfExportService(ImageUtilityService imageUtilities) => _imageUtilities = imageUtilities;

    public byte[] Create(IReadOnlyList<byte[]> images, int jpegQuality = 92)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0) throw new ArgumentException("At least one image is required.", nameof(images));
        if (images.Count > PdfImageDocumentWriter.MaximumPages)
            throw new ArgumentException($"PDF export supports at most {PdfImageDocumentWriter.MaximumPages} images per operation.", nameof(images));

        using var output = new MemoryStream();
        var writer = new PdfImageDocumentSession(output);
        foreach (var image in images) AppendPage(writer, image, jpegQuality);
        writer.Complete();
        return output.ToArray();
    }


    public async Task<byte[]> CreateFromFilesAsync(IReadOnlyList<string> paths, int jpegQuality = 92, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0) throw new ArgumentException("At least one image path is required.", nameof(paths));
        if (paths.Count > PdfImageDocumentWriter.MaximumPages)
            throw new ArgumentException($"PDF export supports at most {PdfImageDocumentWriter.MaximumPages} images per operation.", nameof(paths));

        using var output = new MemoryStream();
        var writer = new PdfImageDocumentSession(output);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ImageFileReader.ReadAsync(path, cancellationToken);
            await Task.Run(() => AppendPage(writer, bytes, jpegQuality), cancellationToken);
        }
        writer.Complete();
        return output.ToArray();
    }

    private static void AppendPage(PdfImageDocumentSession writer, byte[] image, int jpegQuality)
    {
        using var bitmap = BitmapCodec.DecodeForPixelProcessing(image);
        var jpeg = BitmapCodec.EncodeJpeg(bitmap, jpegQuality);
        writer.AddPage(new PdfJpegPage(jpeg, bitmap.Width, bitmap.Height));
    }

    public byte[] CreateContactSheet(IReadOnlyList<byte[]> images, int columns = 3, int spacing = 16, int jpegQuality = 92)
    {
        if (images.Count == 0) throw new ArgumentException("At least one image is required.", nameof(images));
        var sheet = _imageUtilities.Combine(images.Take(100).ToArray(), Magic.Capture.Core.Utilities.ImageCombineMode.Grid, Math.Clamp(spacing, 0, 128), Math.Clamp(columns, 1, 10));
        return Create([sheet], jpegQuality);
    }
}
