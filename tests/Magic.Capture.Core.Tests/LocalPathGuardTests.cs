using Magic.Capture.Core.Storage;

namespace Magic.Capture.Core.Tests;

public sealed class LocalPathGuardTests
{
    [Fact]
    public void ResolveWithinRoot_accepts_normal_relative_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "magic-capture-history");
        var resolved = LocalPathGuard.ResolveWithinRoot(root, Path.Combine("2026", "08", "capture.png"));
        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("2026", "08", "capture.png"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("../../outside.png")]
    public void ResolveWithinRoot_rejects_parent_traversal(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "magic-capture-history");
        Assert.Throws<InvalidDataException>(() => LocalPathGuard.ResolveWithinRoot(root, relative));
    }

    [Fact]
    public void ResolveWithinRoot_rejects_rooted_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "magic-capture-history");
        var rooted = Path.GetFullPath(Path.Combine(root, "..", "outside.png"));
        Assert.Throws<InvalidDataException>(() => LocalPathGuard.ResolveWithinRoot(root, rooted));
    }
}
