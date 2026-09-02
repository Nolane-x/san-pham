using System.Globalization;
using System.Text;

namespace Magic.Capture.Core.Export;

public sealed record PdfJpegPage(byte[] JpegBytes, int Width, int Height);

/// <summary>
/// Writes a small deterministic PDF containing JPEG-backed image pages. The session API emits
/// each page immediately so callers do not need to retain a second collection of encoded JPEGs.
/// </summary>
public static class PdfImageDocumentWriter
{
    public const int MaximumPages = 512;
    public const long MaximumJpegPayloadBytes = 96L * 1024 * 1024;

    public static byte[] Write(IReadOnlyList<PdfJpegPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0 || pages.Count > MaximumPages)
            throw new ArgumentException($"PDF export requires 1 to {MaximumPages} pages.", nameof(pages));

        using var output = new MemoryStream();
        var writer = new PdfImageDocumentSession(output);
        foreach (var page in pages) writer.AddPage(page);
        writer.Complete();
        return output.ToArray();
    }
}

public sealed class PdfImageDocumentSession
{
    private const double PointsPerPixelAt96Dpi = 72d / 96d;
    private const double MaximumPdfPagePoints = 14_400d;

    private readonly Stream _output;
    private readonly List<long> _offsets = [0L, 0L, 0L];
    private readonly List<int> _pageObjectIds = [];
    private long _jpegPayloadBytes;
    private int _nextObjectId = 3;
    private bool _completed;

    public PdfImageDocumentSession(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite || !output.CanSeek)
            throw new ArgumentException("PDF output stream must be writable and seekable.", nameof(output));
        _output = output;
        WriteAscii(_output, "%PDF-1.4\n%MCD\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
    }

    public int PageCount => _pageObjectIds.Count;

    public void AddPage(PdfJpegPage page)
    {
        if (_completed) throw new InvalidOperationException("The PDF session is already complete.");
        ArgumentNullException.ThrowIfNull(page);
        if (_pageObjectIds.Count >= PdfImageDocumentWriter.MaximumPages)
            throw new InvalidOperationException($"PDF export supports at most {PdfImageDocumentWriter.MaximumPages} pages.");
        if (page.Width <= 0 || page.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(page), "PDF page dimensions must be positive.");
        if (page.JpegBytes is not { Length: > 0 })
            throw new ArgumentException("Every PDF page requires non-empty JPEG data.", nameof(page));

        _jpegPayloadBytes = checked(_jpegPayloadBytes + page.JpegBytes.LongLength);
        if (_jpegPayloadBytes > PdfImageDocumentWriter.MaximumJpegPayloadBytes)
            throw new InvalidOperationException("PDF image payload exceeds the safe in-memory document limit.");

        var pageObject = _nextObjectId;
        var imageObject = pageObject + 1;
        var contentObject = pageObject + 2;
        _nextObjectId += 3;
        _pageObjectIds.Add(pageObject);
        EnsureOffsetCapacity(contentObject);

        var (pageWidth, pageHeight) = PageSize(page.Width, page.Height);
        var widthText = Format(pageWidth);
        var heightText = Format(pageHeight);

        WriteObject(pageObject,
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthText} {heightText}] /Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>");

        SetOffset(imageObject);
        WriteAscii(_output, $"{imageObject} 0 obj\n<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.JpegBytes.Length} >>\nstream\n");
        _output.Write(page.JpegBytes, 0, page.JpegBytes.Length);
        WriteAscii(_output, "\nendstream\nendobj\n");

        var content = $"q\n{widthText} 0 0 {heightText} 0 0 cm\n/Im0 Do\nQ\n";
        var contentBytes = Encoding.ASCII.GetBytes(content);
        SetOffset(contentObject);
        WriteAscii(_output, $"{contentObject} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        _output.Write(contentBytes, 0, contentBytes.Length);
        WriteAscii(_output, "endstream\nendobj\n");
    }

    public void Complete()
    {
        if (_completed) return;
        if (_pageObjectIds.Count == 0) throw new InvalidOperationException("At least one PDF page is required.");

        EnsureOffsetCapacity(2);
        var kids = string.Join(' ', _pageObjectIds.Select(id => $"{id} 0 R"));
        WriteObject(2, $"<< /Type /Pages /Kids [{kids}] /Count {_pageObjectIds.Count} >>");

        var objectCount = _nextObjectId - 1;
        var xref = _output.Position;
        WriteAscii(_output, $"xref\n0 {objectCount + 1}\n");
        WriteAscii(_output, "0000000000 65535 f \n");
        for (var id = 1; id <= objectCount; id++)
        {
            if (id >= _offsets.Count || _offsets[id] <= 0)
                throw new InvalidOperationException($"PDF object {id} was not emitted.");
            WriteAscii(_output, $"{_offsets[id]:0000000000} 00000 n \n");
        }
        WriteAscii(_output, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        _completed = true;
    }

    private void WriteObject(int objectId, string body)
    {
        EnsureOffsetCapacity(objectId);
        SetOffset(objectId);
        WriteAscii(_output, $"{objectId} 0 obj\n{body}\nendobj\n");
    }

    private void SetOffset(int objectId)
    {
        EnsureOffsetCapacity(objectId);
        _offsets[objectId] = _output.Position;
    }

    private void EnsureOffsetCapacity(int objectId)
    {
        while (_offsets.Count <= objectId) _offsets.Add(0L);
    }

    private static (double Width, double Height) PageSize(int pixelWidth, int pixelHeight)
    {
        var width = pixelWidth * PointsPerPixelAt96Dpi;
        var height = pixelHeight * PointsPerPixelAt96Dpi;
        var largest = Math.Max(width, height);
        if (largest > MaximumPdfPagePoints)
        {
            var scale = MaximumPdfPagePoints / largest;
            width *= scale;
            height *= scale;
        }
        return (Math.Max(1, width), Math.Max(1, height));
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteAscii(Stream output, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        output.Write(bytes, 0, bytes.Length);
    }
}
