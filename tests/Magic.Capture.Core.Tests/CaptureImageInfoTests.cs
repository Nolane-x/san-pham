using Magic.Capture.Core.Capture;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureImageInfoTests
{
    [Fact]
    public void Png_dimensions_reads_ihdr_without_decoding_pixels()
    {
        var png = CreateHeader(321, 654);
        Assert.True(PngDimensions.TryRead(png, out var width, out var height));
        Assert.Equal(321, width);
        Assert.Equal(654, height);
    }

    [Fact]
    public void Png_dimensions_rejects_invalid_signature_or_zero_size()
    {
        Assert.False(PngDimensions.TryRead(new byte[24], out _, out _));
        Assert.False(PngDimensions.TryRead(CreateHeader(0, 100), out _, out _));
    }

    private static byte[] CreateHeader(int width, int height)
    {
        var bytes = new byte[24];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(bytes, 0);
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
