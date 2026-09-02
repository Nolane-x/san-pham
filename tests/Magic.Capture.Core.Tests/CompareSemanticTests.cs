using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Ocr;
using Xunit;

namespace Magic.Capture.Core.Tests;

public sealed class CompareSemanticTests
{
    [Fact]
    public void Perceptual_hash_is_stable_and_hamming_distance_is_bounded()
    {
        var a = Enumerable.Repeat((byte)0, 9 * 8 * 4).ToArray();
        var b = (byte[])a.Clone();
        for (var i = 0; i < a.Length; i += 4) { a[i + 3] = 255; b[i + 3] = 255; }
        b[(4 * 4 * 9) + 4 * 4 + 2] = 255;
        var ha = PerceptualHash.ComputeDHashBgra(a, 9, 8);
        var hb = PerceptualHash.ComputeDHashBgra(b, 9, 8);
        Assert.InRange(PerceptualHash.HammingDistance(ha, hb), 0, 64);
        Assert.Equal(0, PerceptualHash.HammingDistance(ha, ha));
    }

    [Fact]
    public void Ocr_word_diff_reports_insert_delete_and_equal_with_bounds()
    {
        var left = Doc(("Save", 0), ("file", 50));
        var right = Doc(("Save", 0), ("document", 50));
        var diff = OcrSemanticDiff.Compare(left, right);
        Assert.Contains(diff.Changes, change => change.Kind == OcrWordChangeKind.Removed && change.Text == "file");
        Assert.Contains(diff.Changes, change => change.Kind == OcrWordChangeKind.Added && change.Text == "document");
        Assert.False(diff.IsTruncated);
    }

    [Fact]
    public void Layout_diff_detects_moved_lines()
    {
        var left = new OcrDocument("A", [new OcrLine("A", new PixelRect(0, 0, 40, 20), [])], null);
        var right = new OcrDocument("A", [new OcrLine("A", new PixelRect(30, 0, 40, 20), [])], null);
        var diff = OcrLayoutDiff.Compare(left, right, 200, 100, 200, 100);
        Assert.Single(diff.Changes);
        Assert.True(diff.Changes[0].Moved);
    }

    [Fact]
    public void Content_bounds_ignores_uniform_border_and_finds_inner_content()
    {
        const int width = 10, height = 8;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = pixels[i + 1] = pixels[i + 2] = 240; pixels[i + 3] = 255; }
        for (var y = 2; y < 6; y++) for (var x = 3; x < 8; x++)
        {
            var i = (y * width + x) * 4; pixels[i] = pixels[i + 1] = pixels[i + 2] = 20;
        }
        var bounds = BgraContentBounds.Find(pixels, width, height, 12);
        Assert.Equal(new PixelRect(3, 2, 5, 4), bounds);
    }

    private static OcrDocument Doc(params (string Text, int X)[] words)
    {
        var list = words.Select(w => new OcrWord(w.Text, new PixelRect(w.X, 0, 40, 20))).ToArray();
        return new OcrDocument(string.Join(' ', words.Select(w => w.Text)), [new OcrLine(string.Join(' ', words.Select(w => w.Text)), new PixelRect(0, 0, 120, 20), list)], null);
    }
}
