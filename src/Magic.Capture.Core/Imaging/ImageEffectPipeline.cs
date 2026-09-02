namespace Magic.Capture.Core.Imaging;

public enum ImageEffectKind
{
    Brightness,
    Contrast,
    Gamma,
    Exposure,
    Hue,
    Saturation,
    Vibrance,
    ColorBalance,
    Grayscale,
    Sepia,
    Invert,
    Sharpen,
    NoiseReduction,
    EdgeDetection,
    Posterize,
    Threshold,
    Mosaic,
}

public sealed record ImageEffectStep(
    ImageEffectKind Kind,
    double Amount = 0,
    double SecondaryAmount = 0,
    double TertiaryAmount = 0)
{
    public ImageEffectStep Normalize()
    {
        var amount = double.IsFinite(Amount) ? Amount : DefaultAmount(Kind);
        var secondary = double.IsFinite(SecondaryAmount) ? SecondaryAmount : 0;
        var tertiary = double.IsFinite(TertiaryAmount) ? TertiaryAmount : 0;
        return this with
        {
            Amount = Kind switch
            {
                ImageEffectKind.Brightness or ImageEffectKind.Contrast or ImageEffectKind.Saturation or ImageEffectKind.Vibrance => Math.Clamp(amount, -100, 100),
                ImageEffectKind.Hue => Math.Clamp(amount, -180, 180),
                ImageEffectKind.ColorBalance => Math.Clamp(amount, -100, 100),
                ImageEffectKind.Gamma => Math.Clamp(amount, 0.1, 5),
                ImageEffectKind.Exposure => Math.Clamp(amount, -4, 4),
                ImageEffectKind.Sharpen => Math.Clamp(amount, 0, 5),
                ImageEffectKind.NoiseReduction => Math.Clamp(Math.Round(amount), 1, 4),
                ImageEffectKind.EdgeDetection => Math.Clamp(amount, 0.1, 5),
                ImageEffectKind.Posterize => Math.Clamp(Math.Round(amount), 2, 32),
                ImageEffectKind.Threshold => Math.Clamp(amount, 0, 255),
                ImageEffectKind.Mosaic => Math.Clamp(Math.Round(amount), 2, 64),
                _ => 0,
            },
            SecondaryAmount = Kind == ImageEffectKind.ColorBalance ? Math.Clamp(secondary, -100, 100) : 0,
            TertiaryAmount = Kind == ImageEffectKind.ColorBalance ? Math.Clamp(tertiary, -100, 100) : 0,
        };
    }

    private static double DefaultAmount(ImageEffectKind kind) => kind switch
    {
        ImageEffectKind.Gamma => 1,
        ImageEffectKind.Sharpen => 1,
        ImageEffectKind.NoiseReduction => 1,
        ImageEffectKind.EdgeDetection => 1,
        ImageEffectKind.Posterize => 8,
        ImageEffectKind.Threshold => 128,
        ImageEffectKind.Mosaic => 8,
        _ => 0,
    };
}

public sealed record ImageEffectPipeline(IReadOnlyList<ImageEffectStep> Steps)
{
    public ImageEffectPipeline Normalize()
    {
        var source = Steps ?? Array.Empty<ImageEffectStep>();
        return this with { Steps = source.Where(step => step is not null).Take(32).Select(step => step.Normalize()).ToArray() };
    }
}

public sealed record ImageEffectPreset(string Id, string Name, ImageEffectPipeline Pipeline);

public static class ImageEffectPresets
{
    public static IReadOnlyList<ImageEffectPreset> BuiltIn { get; } =
    [
        new("grayscale", "Grayscale", new ImageEffectPipeline([new(ImageEffectKind.Grayscale)])),
        new("high-contrast", "High contrast", new ImageEffectPipeline([new(ImageEffectKind.Contrast, 28), new(ImageEffectKind.Saturation, 8)])),
        new("soft", "Soft", new ImageEffectPipeline([new(ImageEffectKind.Contrast, -12), new(ImageEffectKind.Brightness, 6), new(ImageEffectKind.Saturation, -8)])),
        new("warm-sepia", "Warm sepia", new ImageEffectPipeline([new(ImageEffectKind.Sepia), new(ImageEffectKind.Contrast, 6)])),
        new("document", "Document", new ImageEffectPipeline([new(ImageEffectKind.Grayscale), new(ImageEffectKind.Contrast, 35), new(ImageEffectKind.Threshold, 185)])),
        new("vivid", "Vivid", new ImageEffectPipeline([new(ImageEffectKind.Vibrance, 28), new(ImageEffectKind.Contrast, 8), new(ImageEffectKind.Sharpen, 1)])),
        new("cool", "Cool", new ImageEffectPipeline([new(ImageEffectKind.ColorBalance, -8, 0, 14), new(ImageEffectKind.Vibrance, 8)])),
        new("clean-ui", "Clean UI", new ImageEffectPipeline([new(ImageEffectKind.NoiseReduction, 1), new(ImageEffectKind.Sharpen, 1), new(ImageEffectKind.Contrast, 6)])),
    ];
}
