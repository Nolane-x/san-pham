namespace Magic.Capture.Core.Commerce;

public sealed record EntitlementSnapshot(
    ProductTier Tier,
    DateTimeOffset EvaluatedUtc,
    DateTimeOffset? TrialEndsUtc,
    TimeSpan TrialRemaining,
    bool StoreConfirmedPro,
    string Source)
{
    public bool CanUse(ProductFeature feature) => FeatureCatalog.CanUse(Tier, feature);
    public bool IsTrial => Tier == ProductTier.PlusTrial;
    public bool IsPro => Tier == ProductTier.ProLifetime;

    public static EntitlementSnapshot Create(ProductTier tier, DateTimeOffset evaluatedUtc, DateTimeOffset? trialEndsUtc, bool storeConfirmedPro, string source)
    {
        var remaining = trialEndsUtc is { } end && end > evaluatedUtc ? end - evaluatedUtc : TimeSpan.Zero;
        return new EntitlementSnapshot(tier, evaluatedUtc, trialEndsUtc, remaining, storeConfirmedPro, source);
    }
}
