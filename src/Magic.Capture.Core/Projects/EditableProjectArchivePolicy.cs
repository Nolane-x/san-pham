using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Projects;

public sealed record ProjectArchiveEntry(string Name, long UncompressedLength);

public static class EditableProjectArchivePolicy
{
    public const string ManifestEntryName = "manifest.json";
    public const string BaseImageEntryName = "base.png";
    public const long MaximumManifestBytes = 16L * 1024 * 1024;
    public const long MaximumBaseImageBytes = 256L * 1024 * 1024;
    public const long MaximumArchiveBytes = MaximumBaseImageBytes + MaximumManifestBytes + 1024L * 1024;

    public static void ValidateArchiveLength(long length)
    {
        if (length <= 0 || length > MaximumArchiveBytes)
            throw new InvalidDataException($"Editable project package exceeds the safe {MaximumArchiveBytes / (1024 * 1024)} MB limit.");
    }

    public static void ValidateBaseImageLength(long length)
    {
        if (length <= 0 || length > MaximumBaseImageBytes)
            throw new InvalidDataException($"Editable project base image exceeds the safe {MaximumBaseImageBytes / (1024 * 1024)} MB limit.");
    }

    public static void ValidateEntries(IReadOnlyList<ProjectArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != 2)
            throw new InvalidDataException("Editable project package must contain exactly manifest.json and base.png.");

        var manifest = entries.Where(entry => string.Equals(entry.Name, ManifestEntryName, StringComparison.Ordinal)).ToArray();
        var image = entries.Where(entry => string.Equals(entry.Name, BaseImageEntryName, StringComparison.Ordinal)).ToArray();
        if (manifest.Length != 1 || image.Length != 1)
            throw new InvalidDataException("Editable project package contains missing or duplicate required entries.");
        if (manifest[0].UncompressedLength <= 0 || manifest[0].UncompressedLength > MaximumManifestBytes)
            throw new InvalidDataException("Editable project manifest exceeds the safe size limit.");
        ValidateBaseImageLength(image[0].UncompressedLength);
    }
}
