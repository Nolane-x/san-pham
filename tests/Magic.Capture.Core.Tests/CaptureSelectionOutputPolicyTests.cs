using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureSelectionOutputPolicyTests
{
    [Fact]
    public void ValidateSeparateRegions_ReturnsTotalPixelsWithinBudget()
    {
        var total = CaptureSelectionOutputPolicy.ValidateSeparateRegions([
            new PixelRect(0, 0, 100, 50),
            new PixelRect(200, 0, 40, 25)
        ]);

        Assert.Equal(6_000, total);
    }

    [Fact]
    public void ValidateSeparateRegions_RejectsEmptyOrTooManyRegions()
    {
        Assert.Throws<InvalidDataException>(() => CaptureSelectionOutputPolicy.ValidateSeparateRegions([]));
        var tooMany = Enumerable.Range(0, CaptureSelectionGeometryRules.MaximumRegions + 1)
            .Select(index => new PixelRect(index * 2, 0, 2, 2))
            .ToArray();
        Assert.Throws<InvalidDataException>(() => CaptureSelectionOutputPolicy.ValidateSeparateRegions(tooMany));
    }

    [Fact]
    public void ValidateSeparateRegions_RejectsTotalPixelBudgetOverflow()
    {
        var regions = Enumerable.Range(0, 5)
            .Select(index => new PixelRect(index * 4_000, 0, 4_000, 2_500))
            .ToArray();

        Assert.Throws<InvalidDataException>(() => CaptureSelectionOutputPolicy.ValidateSeparateRegions(regions));
    }
}
