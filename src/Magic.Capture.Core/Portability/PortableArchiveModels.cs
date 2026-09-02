namespace Magic.Capture.Core.Portability;

public enum PortableArchiveKind
{
    Configuration,
    History
}

public sealed record PortableArchiveEntry(string Name, long Length, string Sha256);

public sealed record PortableArchiveManifest(
    int SchemaVersion,
    string Product,
    string SourceAppVersion,
    DateTimeOffset CreatedUtc,
    PortableArchiveKind Kind,
    IReadOnlyList<PortableArchiveEntry> Entries);

public sealed record PortableArchiveValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static PortableArchiveValidationResult Success { get; } = new(true, []);
}
