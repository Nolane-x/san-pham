using Magic.Capture.Core.Utilities;

namespace Magic.Capture.Core.Tests;

public sealed class DirectoryIndexPolicyTests
{
    [Fact]
    public void NormalizeDisplayName_RemovesNewlinesAndBoundsLength()
    {
        var source = "first\r\n" + new string('x', DirectoryIndexPolicy.MaximumDisplayNameCharacters + 50);
        var normalized = DirectoryIndexPolicy.NormalizeDisplayName(source);

        Assert.DoesNotContain('\r', normalized);
        Assert.DoesNotContain('\n', normalized);
        Assert.Equal(DirectoryIndexPolicy.MaximumDisplayNameCharacters, normalized.Length);
        Assert.EndsWith("…", normalized);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(16 * 1024 * 1024 - 1, 1, true)]
    [InlineData(16 * 1024 * 1024, 1, false)]
    [InlineData(-1, 1, false)]
    [InlineData(0, -1, false)]
    public void CanAppend_EnforcesOutputBudget(int current, int additional, bool expected)
    {
        Assert.Equal(expected, DirectoryIndexPolicy.CanAppend(current, additional));
    }
}
