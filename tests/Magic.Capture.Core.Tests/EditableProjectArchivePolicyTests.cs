using Magic.Capture.Core.Projects;

namespace Magic.Capture.Core.Tests;

public sealed class EditableProjectArchivePolicyTests
{
    [Fact]
    public void AcceptsExactlyOneManifestAndBaseImage()
    {
        EditableProjectArchivePolicy.ValidateArchiveLength(1024);
        EditableProjectArchivePolicy.ValidateEntries([
            new ProjectArchiveEntry("manifest.json", 4096),
            new ProjectArchiveEntry("base.png", 1024 * 1024)
        ]);
    }

    [Fact]
    public void RejectsDuplicateUnknownOrOversizedEntries()
    {
        Assert.Throws<InvalidDataException>(() => EditableProjectArchivePolicy.ValidateEntries([
            new ProjectArchiveEntry("manifest.json", 10),
            new ProjectArchiveEntry("manifest.json", 10),
            new ProjectArchiveEntry("base.png", 10)
        ]));
        Assert.Throws<InvalidDataException>(() => EditableProjectArchivePolicy.ValidateEntries([
            new ProjectArchiveEntry("manifest.json", 10),
            new ProjectArchiveEntry("base.png", 10),
            new ProjectArchiveEntry("extra.bin", 10)
        ]));
        Assert.Throws<InvalidDataException>(() => EditableProjectArchivePolicy.ValidateEntries([
            new ProjectArchiveEntry("manifest.json", EditableProjectArchivePolicy.MaximumManifestBytes + 1),
            new ProjectArchiveEntry("base.png", 10)
        ]));
        Assert.Throws<InvalidDataException>(() => EditableProjectArchivePolicy.ValidateArchiveLength(EditableProjectArchivePolicy.MaximumArchiveBytes + 1));
    }
}

public sealed class EditableProjectArchiveBaseImageLimitTests
{
    [Fact]
    public void RejectsProjectBaseImageAboveDedicatedPackageLimit()
    {
        Assert.Throws<InvalidDataException>(() => EditableProjectArchivePolicy.ValidateEntries([
            new ProjectArchiveEntry("manifest.json", 1024),
            new ProjectArchiveEntry("base.png", EditableProjectArchivePolicy.MaximumBaseImageBytes + 1)
        ]));
    }
}
