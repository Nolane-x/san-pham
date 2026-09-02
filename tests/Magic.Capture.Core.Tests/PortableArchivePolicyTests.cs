using Magic.Capture.Core.Portability;

namespace Magic.Capture.Core.Tests;

public sealed class PortableArchivePolicyTests
{
    [Fact]
    public void Future_schema_is_rejected()
    {
        var manifest = new PortableArchiveManifest(
            PortableArchivePolicy.CurrentSchemaVersion + 1,
            PortableArchivePolicy.ProductName,
            "9.9.9",
            DateTimeOffset.UtcNow,
            PortableArchiveKind.Configuration,
            []);

        var validation = PortableArchivePolicy.ValidateManifest(manifest);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Configuration_inventory_is_exact_allowlist_and_rejects_duplicates()
    {
        var manifest = new PortableArchiveManifest(
            1,
            PortableArchivePolicy.ProductName,
            "3.7.0",
            DateTimeOffset.UtcNow,
            PortableArchiveKind.Configuration,
            [
                new PortableArchiveEntry("settings.json", 10, new string('a', 64)),
                new PortableArchiveEntry("settings.json", 10, new string('a', 64)),
                new PortableArchiveEntry("ai-providers.json", 10, new string('a', 64))
            ]);

        var validation = PortableArchivePolicy.ValidateManifest(manifest);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Errors, error => error.Contains("allow", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../settings.json")]
    [InlineData("/settings.json")]
    [InlineData("images/../../secret.png")]
    [InlineData("images\\secret.png")]
    public void Entry_names_reject_path_traversal_or_noncanonical_separators(string name)
    {
        Assert.False(PortableArchivePolicy.IsCanonicalEntryName(name));
    }

    [Fact]
    public void History_image_name_requires_exact_guid_payload_path()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Assert.True(PortableArchivePolicy.IsHistoryImageEntry($"images/{id:N}.png", out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(PortableArchivePolicy.IsHistoryImageEntry("images/not-a-guid.png", out _));
    }
}
