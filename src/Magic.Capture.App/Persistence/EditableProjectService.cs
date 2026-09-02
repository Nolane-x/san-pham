using System.IO.Compression;
using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Projects;

namespace Magic.Capture.App.Persistence;

internal sealed record EditableProjectPackage(EditableProjectManifest Manifest, byte[] BasePng);

internal sealed class EditableProjectService
{
    private const string ManifestEntry = "manifest.json";
    private const string BaseImageEntry = "base.png";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveAsync(string path, byte[] basePng, EditableProjectManifest manifest, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Project path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentNullException.ThrowIfNull(manifest);
        EditableProjectArchivePolicy.ValidateBaseImageLength(basePng.LongLength);
        var validation = EditableProjectValidator.Validate(manifest);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        if (!PngDimensions.TryRead(basePng, out var width, out var height)) throw new InvalidDataException("Project base image is not a valid PNG.");
        if (width != manifest.Width || height != manifest.Height) throw new InvalidDataException("Project manifest dimensions do not match the base image.");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Project path has no directory.");
        Directory.CreateDirectory(directory);
        var temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, useAsync: true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                var manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Fastest);
                await using (var entryStream = manifestEntry.Open())
                    await JsonSerializer.SerializeAsync(entryStream, manifest with { ModifiedUtc = DateTimeOffset.UtcNow }, JsonOptions, cancellationToken);

                var imageEntry = archive.CreateEntry(BaseImageEntry, CompressionLevel.NoCompression);
                await using (var imageStream = imageEntry.Open())
                    await imageStream.WriteAsync(basePng, cancellationToken);
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<EditableProjectPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Project path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var fileInfo = new FileInfo(fullPath);
        EditableProjectArchivePolicy.ValidateArchiveLength(fileInfo.Length);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        EditableProjectArchivePolicy.ValidateEntries(archive.Entries
            .Select(entry => new ProjectArchiveEntry(entry.FullName, entry.Length))
            .ToArray());
        var manifestEntry = archive.GetEntry(ManifestEntry) ?? throw new InvalidDataException("Project manifest is missing.");
        var imageEntry = archive.GetEntry(BaseImageEntry) ?? throw new InvalidDataException("Project base image is missing.");

        EditableProjectManifest? manifest;
        await using (var manifestStream = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<EditableProjectManifest>(manifestStream, JsonOptions, cancellationToken);
        var validation = EditableProjectValidator.Validate(manifest);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));

        byte[] basePng;
        await using (var imageStream = imageEntry.Open())
            basePng = await BoundedStreamReader.ReadExactAsync(imageStream, imageEntry.Length, EditableProjectArchivePolicy.MaximumBaseImageBytes, cancellationToken);
        EditableProjectArchivePolicy.ValidateBaseImageLength(basePng.LongLength);
        if (!PngDimensions.TryRead(basePng, out var width, out var height)) throw new InvalidDataException("Project base image is not a valid PNG.");
        if (manifest!.Width != width || manifest.Height != height) throw new InvalidDataException("Project base image dimensions do not match the manifest.");
        return new EditableProjectPackage(manifest, basePng);
    }
}
