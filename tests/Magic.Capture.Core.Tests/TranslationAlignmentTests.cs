using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class TranslationAlignmentTests
{
    [Fact]
    public void Finds_horizontal_translation_for_shifted_pattern()
    {
        const int width = 7;
        const int height = 3;
        var first = new byte[width * height * 4];
        var second = new byte[first.Length];
        FillAlpha(first);
        FillAlpha(second);
        Set(first, width, 2, 1, 20, 40, 220);
        Set(first, width, 3, 1, 30, 210, 50);
        Set(second, width, 3, 1, 20, 40, 220);
        Set(second, width, 4, 1, 30, 210, 50);

        var result = TranslationAlignment.FindBestBgra(first, second, width, height, maxOffset: 2, sampleStep: 1);
        Assert.Equal(-1, result.OffsetX);
        Assert.Equal(0, result.OffsetY);
    }

    [Fact]
    public void Search_is_bounded_and_prefers_zero_when_images_match()
    {
        const int width = 4;
        const int height = 4;
        var pixels = Enumerable.Range(0, width * height * 4).Select(i => (byte)(i * 17)).ToArray();
        var result = TranslationAlignment.FindBestBgra(pixels, pixels, width, height, maxOffset: 99, sampleStep: 0);
        Assert.Equal(0, result.OffsetX);
        Assert.Equal(0, result.OffsetY);
        Assert.True(result.ComparedSamples > 0);
    }

    private static void FillAlpha(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
    }

    private static void Set(byte[] pixels, int width, int x, int y, byte b, byte g, byte r)
    {
        var i = (y * width + x) * 4;
        pixels[i] = b;
        pixels[i + 1] = g;
        pixels[i + 2] = r;
        pixels[i + 3] = 255;
    }
}

public sealed class TranslationAlignmentBoundedSearchTests
{
    [Fact]
    public void Large_offset_search_uses_bounded_coarse_to_fine_candidates()
    {
        const int width = 64;
        const int height = 32;
        var first = new byte[width * height * 4];
        var second = new byte[first.Length];
        for (var i = 3; i < first.Length; i += 4) { first[i] = 255; second[i] = 255; }
        for (var y = 5; y < 25; y++)
        for (var x = 8; x < 48; x++)
        {
            var i = (y * width + x) * 4;
            first[i] = (byte)((x * 13 + y * 7) & 255);
            first[i + 1] = (byte)((x * 5 + y * 17) & 255);
            first[i + 2] = (byte)((x * 19 + y * 3) & 255);
            var shiftedX = x + 9;
            var shiftedY = y - 3;
            if (shiftedX < width && shiftedY >= 0)
            {
                var j = (shiftedY * width + shiftedX) * 4;
                second[j] = first[i];
                second[j + 1] = first[i + 1];
                second[j + 2] = first[i + 2];
            }
        }

        var result = TranslationAlignment.FindBestBgra(first, second, width, height, maxOffset: 32, sampleStep: 1);

        Assert.Equal(-9, result.OffsetX);
        Assert.Equal(3, result.OffsetY);
        Assert.InRange(result.EvaluatedOffsetCount, 1, 700);
    }
}
