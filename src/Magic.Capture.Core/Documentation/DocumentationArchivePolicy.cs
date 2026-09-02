namespace Magic.Capture.Core.Documentation;

public sealed record DocumentationArchiveEntry(string Name, long Length);

public static class DocumentationArchivePolicy
{
    public const long MaximumManifestBytes = 4L * 1024 * 1024;
    public const long MaximumImageBytes = 32L * 1024 * 1024;
    public const long MaximumTotalImageBytes = 512L * 1024 * 1024;
    public const long MaximumArchiveBytes = MaximumTotalImageBytes + MaximumManifestBytes + 64L * 1024 * 1024;
    public const int MaximumEntries = DocumentationPolicy.MaximumSteps + 2;

    public static void ValidateArchiveLength(long length)
    {
        if (length <= 0 || length > MaximumArchiveBytes)
            throw new InvalidDataException($"Documentation archive must be between 1 byte and {MaximumArchiveBytes:N0} bytes.");
    }

    public static void ValidateImageLength(long length)
    {
        if (length <= 0 || length > MaximumImageBytes)
            throw new InvalidDataException($"Documentation image exceeds the {MaximumImageBytes:N0}-byte per-image limit.");
    }

    public static bool IsCanonicalEntryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 300) return false;
        if (name.Contains('\\') || name.Contains('\0') || name.Contains(':')) return false;
        if (name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal)) return false;
        if (name.Contains("//", StringComparison.Ordinal)) return false;
        var parts = name.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or "..")) return false;
        if (string.Equals(name, "manifest.json", StringComparison.Ordinal)) return true;
        if (string.Equals(name, "logo.png", StringComparison.Ordinal)) return true;
        return parts.Length == 2 &&
               string.Equals(parts[0], "steps", StringComparison.Ordinal) &&
               parts[1].EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
               parts[1].Length > 4;
    }

    public static void ValidateEntries(IEnumerable<DocumentationArchiveEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var entries = source.ToArray();
        if (entries.Length == 0 || entries.Length > MaximumEntries)
            throw new InvalidDataException($"Documentation archive must contain 1 to {MaximumEntries} entries.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var manifestCount = 0;
        var logoCount = 0;
        var imageCount = 0;
        long imageBytes = 0;
        foreach (var entry in entries)
        {
            if (!IsCanonicalEntryName(entry.Name))
                throw new InvalidDataException($"Documentation archive entry is not canonical: {entry.Name}");
            if (!names.Add(entry.Name))
                throw new InvalidDataException($"Documentation archive contains duplicate entry: {entry.Name}");
            if (entry.Length < 0)
                throw new InvalidDataException("Documentation archive contains a negative entry length.");

            if (string.Equals(entry.Name, "manifest.json", StringComparison.Ordinal))
            {
                manifestCount++;
                if (entry.Length <= 0 || entry.Length > MaximumManifestBytes)
                    throw new InvalidDataException($"Documentation manifest exceeds the {MaximumManifestBytes:N0}-byte limit.");
            }
            else
            {
                if (string.Equals(entry.Name, "logo.png", StringComparison.Ordinal)) logoCount++;
                else imageCount++;
                ValidateImageLength(entry.Length);
                imageBytes = checked(imageBytes + entry.Length);
                if (imageBytes > MaximumTotalImageBytes)
                    throw new InvalidDataException($"Documentation image payload exceeds the {MaximumTotalImageBytes:N0}-byte project limit.");
            }
        }

        if (manifestCount != 1) throw new InvalidDataException("Documentation archive must contain exactly one manifest.json entry.");
        if (logoCount > 1) throw new InvalidDataException("Documentation archive may contain at most one logo.png entry.");
        if (imageCount > DocumentationPolicy.MaximumSteps)
            throw new InvalidDataException($"Documentation archive contains more than {DocumentationPolicy.MaximumSteps} step images.");
    }
}
