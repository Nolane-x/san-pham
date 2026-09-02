using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class BgraTranslationTests
{
    [Fact]
    public void TranslateInPlace_ShiftsRightAndClearsUncoveredPixels()
    {
        const int width = 3;
        const int height = 2;
        var pixels = Pixels(1, 2, 3, 4, 5, 6);

        BgraTranslation.TranslateInPlace(pixels, width, height, offsetX: 1, offsetY: 0);

        Assert.Equal(new byte[] { 0, 1, 2, 0, 4, 5 }, BlueValues(pixels));
        Assert.Equal(new byte[] { 0, 255, 255, 0, 255, 255 }, AlphaValues(pixels));
    }

    [Fact]
    public void TranslateInPlace_ShiftsUpAndLeftWithoutRowCorruption()
    {
        const int width = 3;
        const int height = 3;
        var pixels = Pixels(1, 2, 3, 4, 5, 6, 7, 8, 9);

        BgraTranslation.TranslateInPlace(pixels, width, height, offsetX: -1, offsetY: -1);

        Assert.Equal(new byte[] { 5, 6, 0, 8, 9, 0, 0, 0, 0 }, BlueValues(pixels));
    }

    [Fact]
    public void TranslateInPlace_ClearsCanvasWhenOffsetHasNoOverlap()
    {
        var pixels = Pixels(1, 2, 3, 4);
        BgraTranslation.TranslateInPlace(pixels, 2, 2, offsetX: 2, offsetY: 0);
        Assert.All(pixels, value => Assert.Equal((byte)0, value));
    }

    private static byte[] Pixels(params byte[] blue)
    {
        var result = new byte[blue.Length * 4];
        for (var i = 0; i < blue.Length; i++)
        {
            result[i * 4] = blue[i];
            result[i * 4 + 3] = 255;
        }
        return result;
    }

    private static byte[] BlueValues(byte[] pixels) => pixels.Where((_, index) => index % 4 == 0).ToArray();
    private static byte[] AlphaValues(byte[] pixels) => pixels.Where((_, index) => index % 4 == 3).ToArray();
}
