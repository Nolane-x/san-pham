using System.Text;
using Magic.Capture.App.Export;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Export;

namespace Magic.Capture.App.Documentation;

internal sealed class DocumentationExportService
{
    private readonly DocumentationCardRenderer _renderer;
    private readonly PdfExportService _pdfExport;

    public DocumentationExportService(DocumentationCardRenderer renderer, PdfExportService pdfExport)
    {
        _renderer = renderer;
        _pdfExport = pdfExport;
    }

    public async Task ExportLongPngAsync(
        string path,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExport(project, images, logoPng);
        var bytes = await Task.Run(() => _renderer.RenderLongImage(project, images, logoPng), cancellationToken);
        await AtomicFile.WriteBytesAsync(path, bytes, cancellationToken);
    }

    public async Task ExportPdfAsync(
        string path,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExport(project, images, logoPng);
        var bytes = await Task.Run(() =>
        {
            var includeOverview = project.Steps.Count < PdfImageDocumentWriter.MaximumPages;
            var cards = _renderer.RenderCards(project, images, logoPng, includeOverview);
            return _pdfExport.Create(cards);
        }, cancellationToken);
        await AtomicFile.WriteBytesAsync(path, bytes, cancellationToken);
    }

    public async Task ExportDocxAsync(
        string path,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExport(project, images, logoPng);
        var bytes = await Task.Run(() => DocumentationDocxWriter.Write(project, images, logoPng), cancellationToken);
        await AtomicFile.WriteBytesAsync(path, bytes, cancellationToken);
    }

    public Task ExportHtmlAsync(
        string folderPath,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExport(project, images, logoPng);
        var map = BuildPortableImageMap(project);
        var html = DocumentationTextExport.BuildHtml(project, key => map[key].Href, logoPng is { Length: > 0 } ? "logo.png" : null);
        return PromoteFolderAsync(folderPath, "index.html", html, map, images, logoPng, cancellationToken);
    }

    public Task ExportMarkdownAsync(
        string folderPath,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExport(project, images, logoPng);
        var map = BuildPortableImageMap(project);
        var markdown = DocumentationTextExport.BuildMarkdown(project, key => map[key].Href, logoPng is { Length: > 0 } ? "logo.png" : null);
        return PromoteFolderAsync(folderPath, "README.md", markdown, map, images, logoPng, cancellationToken);
    }

    public async Task ExportOfflineHtmlAsync(
        string path,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExport(project, images, logoPng);
        var html = await Task.Run(() => DocumentationTextExport.BuildSelfContainedHtml(project, images, logoPng), cancellationToken);
        await AtomicFile.WriteBytesAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);
    }

    private static void ValidateExport(DocumentationProject project, IReadOnlyDictionary<string, byte[]> images, byte[]? logoPng)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(images);
        project = DocumentationPolicy.Normalize(project);
        if (project.Steps.Count == 0) throw new InvalidDataException("Documentation export requires at least one step.");
        long total = 0;
        foreach (var step in project.Steps)
        {
            if (!images.TryGetValue(step.ImageKey, out var bytes) || bytes is null || bytes.Length == 0)
                throw new InvalidDataException($"Missing documentation image: {step.ImageKey}");
            DocumentationArchivePolicy.ValidateImageLength(bytes.LongLength);
            total = checked(total + bytes.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("Documentation export image payload exceeds the project safety limit.");
        }
        if (logoPng is { Length: > 0 })
        {
            DocumentationArchivePolicy.ValidateImageLength(logoPng.LongLength);
            total = checked(total + logoPng.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("Documentation export image payload exceeds the project safety limit.");
            if (!PngDimensions.TryRead(logoPng, out var width, out var height) || width <= 0 || height <= 0)
                throw new InvalidDataException("Documentation logo is not a valid PNG.");
        }
    }

    private static Dictionary<string, PortableImage> BuildPortableImageMap(DocumentationProject project)
    {
        var map = new Dictionary<string, PortableImage>(StringComparer.Ordinal);
        for (var i = 0; i < project.Steps.Count; i++)
        {
            var step = project.Steps[i];
            var fileName = $"step-{i + 1:D3}-{SanitizeFilePart(step.Id)}.png";
            map[step.ImageKey] = new PortableImage(Path.Combine("images", fileName), "images/" + fileName);
        }
        return map;
    }

    private static async Task PromoteFolderAsync(
        string folderPath,
        string documentName,
        string documentContent,
        IReadOnlyDictionary<string, PortableImage> map,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Export folder is required.", nameof(folderPath));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Export folder has no parent directory.");
        Directory.CreateDirectory(parent);
        var stage = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = destination + ".bak-" + Guid.NewGuid().ToString("N");
        var movedExisting = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "images"));
            await File.WriteAllTextAsync(Path.Combine(stage, documentName), documentContent, new UTF8Encoding(false), cancellationToken);
            foreach (var pair in map)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(stage, pair.Value.RelativePath);
                await File.WriteAllBytesAsync(target, images[pair.Key], cancellationToken);
            }
            if (logoPng is { Length: > 0 })
                await File.WriteAllBytesAsync(Path.Combine(stage, "logo.png"), logoPng, cancellationToken);

            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
                movedExisting = true;
            }
            Directory.Move(stage, destination);
            if (movedExisting) Directory.Delete(backup, recursive: true);
        }
        catch
        {
            if (movedExisting && !Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }
        finally
        {
            if (Directory.Exists(stage)) TryDeleteDirectory(stage);
            if (Directory.Exists(backup) && Directory.Exists(destination)) TryDeleteDirectory(backup);
        }
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? string.Empty).Where(c => !invalid.Contains(c) && c != '/' && c != '\\').Take(80).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record PortableImage(string RelativePath, string Href);
}
