using Magic.Capture.Core.Utilities;

namespace Magic.Capture.Core.Tests;

public sealed class Base64ClipboardPolicyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void ComputeBase64CharacterCount_UsesFourThirdsEncoding(long bytes, long expected)
    {
        Assert.Equal(expected, Base64ClipboardPolicy.ComputeBase64CharacterCount(bytes));
    }

    [Fact]
    public void ValidateSourceLength_RejectsBeforeHugeStringAllocation()
    {
        var maximumSource = (Base64ClipboardPolicy.MaximumOutputCharacters / 4L) * 3L;
        Base64ClipboardPolicy.ValidateSourceLength(maximumSource);
        Assert.Throws<InvalidDataException>(() => Base64ClipboardPolicy.ValidateSourceLength(maximumSource + 3));
    }

    [Fact]
    public void ValidateSourceLength_AccountsForDataUriPrefix()
    {
        var maximumSource = (Base64ClipboardPolicy.MaximumOutputCharacters / 4L) * 3L;
        Assert.Throws<InvalidDataException>(() => Base64ClipboardPolicy.ValidateSourceLength(maximumSource, 22));
    }
}
