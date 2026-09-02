using System.Text;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Utilities;

namespace Magic.Capture.Core.Tests;

public sealed class UtilityCoreTests
{
    [Fact]
    public void Sha256_matches_known_vector()
    {
        var digest = HashUtility.ComputeSha256(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", digest);
    }

    [Fact]
    public void Horizontal_combine_places_images_without_overlap()
    {
        var placements = ImageCombineLayout.Create(
            [(100, 40), (50, 60), (20, 10)],
            ImageCombineMode.Horizontal,
            spacing: 4);

        Assert.Equal(3, placements.Count);
        Assert.Equal(new PixelRect(0, 0, 100, 40), placements[0]);
        Assert.Equal(new PixelRect(104, 0, 50, 60), placements[1]);
        Assert.Equal(new PixelRect(158, 0, 20, 10), placements[2]);
    }

    [Fact]
    public void Split_plan_covers_source_without_negative_rectangles()
    {
        var rects = ImageSplitPlan.Create(101, 51, rows: 2, columns: 3);
        Assert.Equal(6, rects.Count);
        Assert.All(rects, r =>
        {
            Assert.True(r.Width > 0);
            Assert.True(r.Height > 0);
            Assert.True(r.X >= 0 && r.Y >= 0);
            Assert.True(r.Right <= 101 && r.Bottom <= 51);
        });
        Assert.Equal(101 * 51, rects.Sum(r => r.Width * r.Height));
    }

    [Fact]
    public void Beautify_options_clamp_invalid_values()
    {
        var normalized = new BeautifyOptions(-2, 9000, 999, -8, 3.0).Normalize();
        Assert.Equal(0, normalized.Padding);
        Assert.Equal(512, normalized.CornerRadius);
        Assert.Equal(256, normalized.ShadowBlur);
        Assert.Equal(0, normalized.BorderWidth);
        Assert.Equal(1.0, normalized.ShadowOpacity);
    }
}
