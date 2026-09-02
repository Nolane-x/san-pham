using Magic.Capture.Core.Documentation;

namespace Magic.Capture.Core.Tests;

public sealed class DocumentationArchivePolicyTests
{
    [Theory]
    [InlineData("manifest.json", true)]
    [InlineData("logo.png", true)]
    [InlineData("steps/0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("../manifest.json", false)]
    [InlineData("/manifest.json", false)]
    [InlineData("steps\\x.png", false)]
    [InlineData("C:/x.png", false)]
    [InlineData("steps//x.png", false)]
    public void IsCanonicalEntryName_RejectsTraversalAndNonCanonicalPaths(string name, bool expected)
    {
        Assert.Equal(expected, DocumentationArchivePolicy.IsCanonicalEntryName(name));
    }

    [Fact]
    public void ValidateEntries_RejectsDuplicateEntries()
    {
        var entries = new[]
        {
            new DocumentationArchiveEntry("manifest.json", 100),
            new DocumentationArchiveEntry("steps/a.png", 100),
            new DocumentationArchiveEntry("steps/a.png", 100)
        };

        Assert.Throws<InvalidDataException>(() => DocumentationArchivePolicy.ValidateEntries(entries));
    }

    [Fact]
    public void ValidateEntries_RejectsOversizeImage()
    {
        var entries = new[]
        {
            new DocumentationArchiveEntry("manifest.json", 100),
            new DocumentationArchiveEntry("steps/a.png", DocumentationArchivePolicy.MaximumImageBytes + 1)
        };

        Assert.Throws<InvalidDataException>(() => DocumentationArchivePolicy.ValidateEntries(entries));
    }

    [Fact]
    public void ValidateEntries_AcceptsBoundedCanonicalPackage()
    {
        var entries = new[]
        {
            new DocumentationArchiveEntry("manifest.json", 1024),
            new DocumentationArchiveEntry("steps/a.png", 2048),
            new DocumentationArchiveEntry("steps/b.png", 4096),
            new DocumentationArchiveEntry("logo.png", 512)
        };

        DocumentationArchivePolicy.ValidateEntries(entries);
    }
}
