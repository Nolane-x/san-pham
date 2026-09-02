using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Portability;

public static class PortableArchivePolicy
{
    public const int CurrentSchemaVersion = 1;
    public const string ProductName = "Magic Capture Desktop";
    public const string ManifestEntryName = "manifest.json";
    public const string HistoryMetadataEntryName = "history.json";
    public const int MaximumConfigurationEntries = 6;
    public const int MaximumHistoryCapturesPerArchive = 20_000;
    public const int MaximumHistoryPayloadEntries = MaximumHistoryCapturesPerArchive + 1;
    public const long MaximumConfigurationPayloadBytes = 16L * 1024 * 1024;
    public const long MaximumHistoryArchiveBytes = 8L * 1024 * 1024 * 1024;
    public const long MaximumManifestBytes = 8L * 1024 * 1024;
    public const long MaximumHistoryMetadataBytes = 128L * 1024 * 1024;

    private static readonly HashSet<string> ConfigurationAllowlist = new(StringComparer.Ordinal)
    {
        "settings.json",
        "workflows.json",
        "destinations.json",
        "local-actions.json",
        "magic-actions.json",
        "magic-recipes.json"
    };

    public static IReadOnlyCollection<string> AllowedConfigurationEntries => ConfigurationAllowlist;

    public static PortableArchiveValidationResult ValidateManifest(PortableArchiveManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();
        if (manifest.SchemaVersion != CurrentSchemaVersion)
            errors.Add($"Unsupported archive schema {manifest.SchemaVersion}; this version supports schema {CurrentSchemaVersion} only.");
        if (!string.Equals(manifest.Product, ProductName, StringComparison.Ordinal))
            errors.Add("Archive product identity does not match Magic Capture Desktop.");
        if (string.IsNullOrWhiteSpace(manifest.SourceAppVersion) || manifest.SourceAppVersion.Length > 64)
            errors.Add("Archive source app version is missing or invalid.");
        if (!Enum.IsDefined(typeof(PortableArchiveKind), manifest.Kind))
            errors.Add("Archive kind is invalid.");

        var entries = manifest.Entries ?? [];
        var maximumEntries = manifest.Kind == PortableArchiveKind.Configuration
            ? MaximumConfigurationEntries
            : MaximumHistoryPayloadEntries;
        if (entries.Count > maximumEntries)
            errors.Add($"Archive inventory contains too many payload entries ({entries.Count:N0} > {maximumEntries:N0}).");

        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                errors.Add("Archive inventory contains a null entry.");
                continue;
            }
            if (!IsCanonicalEntryName(entry.Name))
                errors.Add($"Archive entry name is not canonical or path-safe: {entry.Name}");
            else if (!names.Add(entry.Name))
                errors.Add($"Archive inventory contains duplicate entry name: {entry.Name}");

            if (entry.Length <= 0)
                errors.Add($"Archive entry is empty or has invalid length: {entry.Name}");
            if (!IsSha256(entry.Sha256))
                errors.Add($"Archive entry SHA-256 is invalid: {entry.Name}");

            if (manifest.Kind == PortableArchiveKind.Configuration)
            {
                if (!ConfigurationAllowlist.Contains(entry.Name))
                    errors.Add($"Configuration entry is not on the export/import allowlist: {entry.Name}");
                if (entry.Length > MaximumConfigurationPayloadBytes)
                    errors.Add($"Configuration payload exceeds the per-entry safety budget: {entry.Name}");
            }
            else
            {
                if (string.Equals(entry.Name, HistoryMetadataEntryName, StringComparison.Ordinal))
                {
                    if (entry.Length > MaximumHistoryMetadataBytes)
                        errors.Add("History metadata exceeds its safety budget.");
                }
                else if (IsHistoryImageEntry(entry.Name, out _))
                {
                    if (entry.Length > ImageWorkloadLimits.MaximumEncodedBytes)
                        errors.Add($"History image exceeds the supported encoded-image limit: {entry.Name}");
                }
                else
                {
                    errors.Add($"History archive contains a payload outside the exact allowlist: {entry.Name}");
                }
            }

            total = SaturatingAdd(total, Math.Max(0, entry.Length));
        }

        if (manifest.Kind == PortableArchiveKind.Configuration && total > MaximumConfigurationPayloadBytes)
            errors.Add("Configuration archive exceeds the cumulative payload safety budget.");
        if (manifest.Kind == PortableArchiveKind.History)
        {
            if (!names.Contains(HistoryMetadataEntryName)) errors.Add("History archive is missing history.json.");
            if (total > MaximumHistoryArchiveBytes) errors.Add("History archive exceeds the cumulative payload safety budget.");
        }

        return errors.Count == 0 ? PortableArchiveValidationResult.Success : new(false, errors);
    }

    public static bool IsCanonicalEntryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 260) return false;
        if (name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal)) return false;
        if (name.Contains('\\') || name.Contains(':') || name.Contains('\0')) return false;
        var parts = name.Split('/');
        return parts.Length > 0 && parts.All(part => !string.IsNullOrWhiteSpace(part) && part is not "." and not "..");
    }

    public static bool IsHistoryImageEntry(string? name, out Guid id)
    {
        id = Guid.Empty;
        if (!IsCanonicalEntryName(name) || name is null) return false;
        const string prefix = "images/";
        const string suffix = ".png";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var token = name[prefix.Length..^suffix.Length];
        return token.Length == 32 && Guid.TryParseExact(token, "N", out id);
    }

    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static long SaturatingAdd(long left, long right) => long.MaxValue - left < right ? long.MaxValue : left + right;
}
