namespace Magic.Capture.Core.Commerce;

public static class FeatureCatalog
{
    private static readonly HashSet<ProductFeature> FreeFeatures =
    [
        ProductFeature.BasicCapture,
        ProductFeature.BasicOcr,
        ProductFeature.BasicPin,
        ProductFeature.BasicEditor,
        ProductFeature.BasicHistory,
        ProductFeature.BasicWorkflows,
        ProductFeature.AutoCapture,
        ProductFeature.UtilityMetadataAndHashes,
        ProductFeature.UtilityBeautifyBasic
    ];

    private static readonly HashSet<ProductFeature> PlusFeatures =
    [
        .. FreeFeatures,
        ProductFeature.TableExtraction,
        ProductFeature.BarcodeRecognition,
        ProductFeature.ScrollingStitch,
        ProductFeature.AdvancedEditor,
        ProductFeature.AdvancedImageExport,
        ProductFeature.UnlimitedPins,
        ProductFeature.DirectRecognitionActions,
        ProductFeature.AdvancedWorkflows,
        ProductFeature.ChangeAwareCaptureWatch,
        ProductFeature.UtilityImagePack,
        ProductFeature.CliAutomation
    ];

    public static bool CanUse(ProductTier tier, ProductFeature feature) => tier switch
    {
        ProductTier.ProLifetime => true,
        ProductTier.PlusTrial => PlusFeatures.Contains(feature),
        _ => FreeFeatures.Contains(feature)
    };

    public static ProductTier RequiredTier(ProductFeature feature)
    {
        if (FreeFeatures.Contains(feature)) return ProductTier.Free;
        if (PlusFeatures.Contains(feature)) return ProductTier.PlusTrial;
        return ProductTier.ProLifetime;
    }
}
