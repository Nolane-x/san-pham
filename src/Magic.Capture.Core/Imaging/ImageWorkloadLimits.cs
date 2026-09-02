namespace Magic.Capture.Core.Imaging;

public static class ImageWorkloadLimits
{
    // Long scrolling captures are intentionally supported, but a single decoded image still has
    // a hard ceiling. Workloads that duplicate pixel buffers use stricter limits below.
    public const int MaximumDimension = 200_000;
    public const long MaximumPixelCount = 150_000_000;
    public const long MaximumEncodedBytes = 512L * 1024 * 1024;

    // Full-frame pixel effects may hold the bitmap plus one or two BGRA working buffers.
    // 40M pixels keeps 8K (33.2M) supported while rejecting unusually large full-frame effects.
    public const long MaximumPixelProcessingPixelCount = 40_000_000;

    // Compare keeps two normalized canvases, two raw BGRA buffers and generated map buffers.
    // Keep the interactive path materially below the general decode limit to prevent multi-GB peaks.
    public const long MaximumComparePixelCount = 24_000_000;

    // UI operations that materialize multiple encoded History items at once must stay bounded.
    // Large batch jobs should be processed in smaller selections instead of retaining gigabytes.
    public const long MaximumResidentSelectionEncodedBytes = 128L * 1024 * 1024;

    public static void ValidateEncodedLength(long byteLength)
    {
        if (byteLength <= 0) throw new InvalidDataException("Image payload is empty.");
        if (byteLength > MaximumEncodedBytes)
            throw new InvalidDataException($"Image payload exceeds the supported limit of {MaximumEncodedBytes / (1024 * 1024):N0} MB.");
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException("Image dimensions must be positive.");
        if (width > MaximumDimension || height > MaximumDimension)
            throw new InvalidDataException($"Image dimension exceeds the supported limit of {MaximumDimension:N0} pixels.");
        if (checked((long)width * height) > MaximumPixelCount)
            throw new InvalidDataException($"Image pixel area exceeds the supported limit of {MaximumPixelCount:N0} pixels.");
    }

    public static void ValidatePixelProcessingDimensions(int width, int height)
    {
        ValidateDimensions(width, height);
        if (checked((long)width * height) > MaximumPixelProcessingPixelCount)
            throw new InvalidDataException($"This pixel-processing operation exceeds the safe working-set limit of {MaximumPixelProcessingPixelCount:N0} pixels. Crop or resize the image first.");
    }

    public static void ValidateCompareDimensions(int width, int height)
    {
        ValidateDimensions(width, height);
        if (checked((long)width * height) > MaximumComparePixelCount)
            throw new InvalidDataException($"Compare canvas exceeds the safe working-set limit of {MaximumComparePixelCount:N0} pixels. Resize or crop the images first.");
    }

    public static void ValidateResidentSelectionBytes(long byteLength)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        if (byteLength > MaximumResidentSelectionEncodedBytes)
            throw new InvalidDataException($"The selected images exceed the safe in-memory batch limit of {MaximumResidentSelectionEncodedBytes / (1024 * 1024):N0} MB. Process a smaller selection.");
    }
}
