using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Capture;

public static class CaptureSelectionOutputPolicy
{
    public const long MaximumSeparateRegionPixels = ImageWorkloadLimits.MaximumPixelProcessingPixelCount;

    public static long ValidateSeparateRegions(IReadOnlyList<PixelRect>? regions)
    {
        if (regions is null || regions.Count is < 1 or > CaptureSelectionGeometryRules.MaximumRegions)
            throw new InvalidDataException($"Separate multi-region output requires 1 to {CaptureSelectionGeometryRules.MaximumRegions} regions.");

        long totalPixels = 0;
        foreach (var region in regions)
        {
            if (region.IsEmpty)
                throw new InvalidDataException("Separate multi-region output contains an empty region.");
            ImageWorkloadLimits.ValidatePixelProcessingDimensions(region.Width, region.Height);
            totalPixels = checked(totalPixels + checked((long)region.Width * region.Height));
            if (totalPixels > MaximumSeparateRegionPixels)
                throw new InvalidDataException($"Separate multi-region output exceeds the safe working-set limit of {MaximumSeparateRegionPixels:N0} total pixels. Use Canvas output or select smaller regions.");
        }

        return totalPixels;
    }
}
