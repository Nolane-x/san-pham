using Magic.Capture.Core.Platform;

namespace Magic.Capture.Core.Tests;

public sealed class ClipboardPreviewPolicyTests
{
    [Fact]
    public void BoundedCharacterCount_CapsHugeClipboardWithoutAllocatingForItsFullSize()
    {
        var count = ClipboardPreviewPolicy.BoundedCharacterCount(500_000_000);
        Assert.Equal(ClipboardPreviewPolicy.MaximumTextPreviewCharacters, count);
    }

    [Fact]
    public void BoundedCharacterCount_UsesAvailableUnicodeCharactersForSmallPayload()
    {
        Assert.Equal(5, ClipboardPreviewPolicy.BoundedCharacterCount(10));
    }

    [Fact]
    public void BoundedCharacterCount_RejectsPreviewLimitAboveProductBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipboardPreviewPolicy.BoundedCharacterCount(10, 20_000));
    }
}
