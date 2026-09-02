using Magic.Capture.Core.Utilities;

namespace Magic.Capture.Core.Tests;

public sealed class GeneratedCodeInputPolicyTests
{
    [Fact]
    public void QrInputIsTrimmedAndBoundedByUtf8Bytes()
    {
        Assert.Equal("hello", GeneratedCodeInputPolicy.NormalizeQr("  hello  "));
        var oversized = new string('界', 1000);
        Assert.Throws<ArgumentException>(() => GeneratedCodeInputPolicy.NormalizeQr(oversized));
    }

    [Fact]
    public void Code128InputIsTrimmedAndBounded()
    {
        Assert.Equal("ABC-123", GeneratedCodeInputPolicy.NormalizeCode128(" ABC-123 "));
        Assert.Throws<ArgumentException>(() => GeneratedCodeInputPolicy.NormalizeCode128(new string('A', 513)));
    }

    [Fact]
    public void EmptyGeneratorInputIsRejected()
    {
        Assert.Throws<ArgumentException>(() => GeneratedCodeInputPolicy.NormalizeQr("   "));
        Assert.Throws<ArgumentException>(() => GeneratedCodeInputPolicy.NormalizeCode128(""));
    }
}
