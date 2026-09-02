namespace Magic.Capture.Core.Ocr;

public readonly record struct OcrResizePlan(
    int SourceWidth,
    int SourceHeight,
    int TargetWidth,
    int TargetHeight)
{
    public bool RequiresResize => SourceWidth != TargetWidth || SourceHeight != TargetHeight;
    public double ScaleXToOriginal => SourceWidth / (double)TargetWidth;
    public double ScaleYToOriginal => SourceHeight / (double)TargetHeight;

    public static OcrResizePlan Create(int width, int height, int maximumDimension)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (maximumDimension <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDimension));

        var longest = Math.Max(width, height);
        if (longest <= maximumDimension)
            return new OcrResizePlan(width, height, width, height);

        var scale = maximumDimension / (double)longest;
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        return new OcrResizePlan(width, height, targetWidth, targetHeight);
    }
}
