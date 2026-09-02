using System.IO.Compression;
using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Documentation;

namespace Magic.Capture.App.Documentation;

internal sealed record DocumentationProjectPackage(
    DocumentationProject Project,
    IReadOnlyDictionary<string, byte[]> Images,
    byte[]? LogoPng = null);

internal sealed class DocumentationProjectStore
{
    private const string ManifestEntryName = "manifest.json";
    private const string LogoEntryName = "logo.png";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveAsync(
        string path,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Documentation project path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(images);

        var normalized = DocumentationPolicy.Normalize(project with { ModifiedUtc = DateTimeOffset.UtcNow });
        ValidateProjectAssets(normalized, images, logoPng);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions);
        if (manifestBytes.LongLength <= 0 || manifestBytes.LongLength > DocumentationArchivePolicy.MaximumManifestBytes)
            throw new InvalidDataException($"Documentation manifest exceeds the {DocumentationArchivePolicy.MaximumManifestBytes:N0}-byte limit.");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Documentation project path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
                await using (var manifestStream = manifestEntry.Open())
                    await manifestStream.WriteAsync(manifestBytes, cancellationToken);

                var writtenImages = new HashSet<string>(StringComparer.Ordinal);
                foreach (var step in normalized.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!writtenImages.Add(step.ImageKey)) continue;
                    var bytes = images[step.ImageKey];
                    var entry = archive.CreateEntry(step.ImageKey, CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes, cancellationToken);
                }

                if (logoPng is not null)
                {
                    var entry = archive.CreateEntry(LogoEntryName, CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(logoPng, cancellationToken);
                }
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    public async Task<DocumentationProjectPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Documentation project path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists) throw new FileNotFoundException("Documentation project was not found.", fullPath);
        DocumentationArchivePolicy.ValidateArchiveLength(fileInfo.Length);

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        DocumentationArchivePolicy.ValidateEntries(archive.Entries
            .Select(entry => new DocumentationArchiveEntry(entry.FullName, entry.Length))
            .ToArray());

        var manifestEntry = archive.GetEntry(ManifestEntryName) ?? throw new InvalidDataException("Documentation manifest is missing.");
        byte[] manifestBytes;
        await using (var manifestStream = manifestEntry.Open())
            manifestBytes = await BoundedStreamReader.ReadExactAsync(
                manifestStream,
                manifestEntry.Length,
                DocumentationArchivePolicy.MaximumManifestBytes,
                cancellationToken);

        var project = JsonSerializer.Deserialize<DocumentationProject>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("Documentation manifest is invalid.");
        if (project.SchemaVersion != DocumentationProject.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported documentation schema {project.SchemaVersion}; this build edits schema {DocumentationProject.CurrentSchemaVersion} only.");
        project = DocumentationPolicy.Normalize(project);

        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var step in project.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (images.TryGetValue(step.ImageKey, out var existing))
            {
                ValidateStepImage(step, existing);
                continue;
            }
            var imageEntry = archive.GetEntry(step.ImageKey)
                ?? throw new InvalidDataException($"Documentation image is missing: {step.ImageKey}");
            byte[] bytes;
            await using (var imageStream = imageEntry.Open())
                bytes = await BoundedStreamReader.ReadExactAsync(
                    imageStream,
                    imageEntry.Length,
                    DocumentationArchivePolicy.MaximumImageBytes,
                    cancellationToken);
            ValidateStepImage(step, bytes);
            images.Add(step.ImageKey, bytes);
        }

        byte[]? logo = null;
        if (archive.GetEntry(LogoEntryName) is { } logoEntry)
        {
            await using var logoStream = logoEntry.Open();
            logo = await BoundedStreamReader.ReadExactAsync(
                logoStream,
                logoEntry.Length,
                DocumentationArchivePolicy.MaximumImageBytes,
                cancellationToken);
            ValidatePng(logo, "Documentation logo");
        }

        ValidateProjectAssets(project, images, logo);
        return new DocumentationProjectPackage(project, images, logo);
    }

    private static void ValidateProjectAssets(
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng)
    {
        if (project.SchemaVersion != DocumentationProject.CurrentSchemaVersion)
            throw new InvalidDataException("Documentation project schema is not writable by this build.");
        if (project.Steps.Count > DocumentationPolicy.MaximumSteps)
            throw new InvalidDataException($"Documentation projects support at most {DocumentationPolicy.MaximumSteps} steps.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var imageKeys = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var step in project.Steps)
        {
            if (!ids.Add(step.Id)) throw new InvalidDataException($"Duplicate documentation step id: {step.Id}");
            if (!DocumentationArchivePolicy.IsCanonicalEntryName(step.ImageKey) ||
                !step.ImageKey.StartsWith("steps/", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid documentation image key: {step.ImageKey}");
            if (!images.TryGetValue(step.ImageKey, out var bytes))
                throw new InvalidDataException($"Missing documentation image: {step.ImageKey}");
            DocumentationArchivePolicy.ValidateImageLength(bytes.LongLength);
            if (imageKeys.Add(step.ImageKey))
            {
                total = checked(total + bytes.LongLength);
                if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                    throw new InvalidDataException("Documentation image payload exceeds the project safety limit.");
            }
            ValidateStepImage(step, bytes);
        }

        if (images.Keys.Any(key => !imageKeys.Contains(key)))
            throw new InvalidDataException("Documentation package contains an image that is not referenced by a step.");
        if (logoPng is null && project.LogoImageKey is not null)
            throw new InvalidDataException("Documentation manifest references a logo that is not present.");
        if (logoPng is not null && !string.Equals(project.LogoImageKey, LogoEntryName, StringComparison.Ordinal))
            throw new InvalidDataException("Documentation package contains a logo that is not referenced by the manifest.");
        if (logoPng is not null)
        {
            DocumentationArchivePolicy.ValidateImageLength(logoPng.LongLength);
            total = checked(total + logoPng.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("Documentation image payload exceeds the project safety limit.");
            ValidatePng(logoPng, "Documentation logo");
        }
    }

    private static void ValidateStepImage(DocumentationStep step, byte[] bytes)
    {
        if (!PngDimensions.TryRead(bytes, out var width, out var height))
            throw new InvalidDataException($"Documentation image is not a valid PNG: {step.ImageKey}");
        if (width != step.Width || height != step.Height)
            throw new InvalidDataException($"Documentation image dimensions do not match the manifest: {step.ImageKey}");
    }

    private static void ValidatePng(byte[] bytes, string label)
    {
        if (!PngDimensions.TryRead(bytes, out _, out _)) throw new InvalidDataException(label + " is not a valid PNG.");
    }
}
