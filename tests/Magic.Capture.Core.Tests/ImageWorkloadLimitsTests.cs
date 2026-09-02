using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class ImageWorkloadLimitsTests
{
    [Fact]
    public void AcceptsLongButReasonableDesktopCapture()
    {
        ImageWorkloadLimits.ValidateDimensions(1000, 100000);
    }

    [Fact]
    public void RejectsUnsafePixelAreaAndDimensions()
    {
        Assert.Throws<InvalidDataException>(() => ImageWorkloadLimits.ValidateDimensions(200000, 200000));
        Assert.Throws<InvalidDataException>(() => ImageWorkloadLimits.ValidateDimensions(200001, 1));
        Assert.Throws<InvalidDataException>(() => ImageWorkloadLimits.ValidateDimensions(0, 100));
    }

    [Fact]
    public void CompareHasASeparateInteractiveWorkingSetLimit()
    {
        ImageWorkloadLimits.ValidateCompareDimensions(5120, 2880);
        Assert.Throws<InvalidDataException>(() => ImageWorkloadLimits.ValidateCompareDimensions(7680, 4320));
    }

    [Fact]
    public void PixelProcessingAllows8kButRejectsOversizedFullFrameBuffers()
    {
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(7680, 4320);
        Assert.Throws<InvalidDataException>(() => ImageWorkloadLimits.ValidatePixelProcessingDimensions(10000, 5000));
    }

    [Fact]
    public void ResidentSelectionBudgetIsBounded()
    {
        ImageWorkloadLimits.ValidateResidentSelectionBytes(64L * 1024 * 1024);
        Assert.Throws<InvalidDataException>(() => ImageWorkloadLimits.ValidateResidentSelectionBytes(ImageWorkloadLimits.MaximumResidentSelectionEncodedBytes + 1));
    }
}
