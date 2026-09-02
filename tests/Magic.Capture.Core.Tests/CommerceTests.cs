using Magic.Capture.Core.Commerce;

namespace Magic.Capture.Core.Tests;

public sealed class CommerceTests
{
    [Fact]
    public void Free_cannot_use_plus_or_pro_features()
    {
        Assert.True(FeatureCatalog.CanUse(ProductTier.Free, ProductFeature.BasicCapture));
        Assert.True(FeatureCatalog.CanUse(ProductTier.Free, ProductFeature.BasicOcr));
        Assert.True(FeatureCatalog.CanUse(ProductTier.Free, ProductFeature.BasicWorkflows));
        Assert.True(FeatureCatalog.CanUse(ProductTier.Free, ProductFeature.UtilityMetadataAndHashes));
        Assert.False(FeatureCatalog.CanUse(ProductTier.Free, ProductFeature.TableExtraction));
        Assert.False(FeatureCatalog.CanUse(ProductTier.Free, ProductFeature.RepeatLastRegion));
    }

    [Fact]
    public void Plus_trial_can_use_plus_but_not_pro_features()
    {
        Assert.True(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.TableExtraction));
        Assert.True(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.BarcodeRecognition));
        Assert.True(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.AdvancedEditor));
        Assert.False(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.RepeatLastRegion));
        Assert.True(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.AdvancedWorkflows));
        Assert.True(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.UtilityImagePack));
        Assert.False(FeatureCatalog.CanUse(ProductTier.PlusTrial, ProductFeature.CompareWorkspace));
    }

    [Fact]
    public void Pro_can_use_every_catalog_feature()
    {
        foreach (var feature in Enum.GetValues<ProductFeature>())
            Assert.True(FeatureCatalog.CanUse(ProductTier.ProLifetime, feature));
    }

    [Fact]
    public void Plus_trial_is_active_for_exactly_168_hours()
    {
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var state = TrialState.Create(start);

        var before = TrialClock.Evaluate(state, start.AddHours(167).AddMinutes(59));
        var atExpiry = TrialClock.Evaluate(state, start.AddHours(168));

        Assert.True(before.IsActive);
        Assert.Equal(ProductTier.PlusTrial, before.Tier);
        Assert.False(atExpiry.IsActive);
        Assert.Equal(ProductTier.Free, atExpiry.Tier);
    }

    [Fact]
    public void Trial_clock_never_moves_backwards_when_system_clock_is_rolled_back()
    {
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var state = TrialState.Create(start) with { LastSeenUtc = start.AddHours(100) };

        var evaluation = TrialClock.Evaluate(state, start.AddHours(5));

        Assert.Equal(start.AddHours(100), evaluation.EffectiveNowUtc);
        Assert.Equal(start.AddHours(100), evaluation.UpdatedState.LastSeenUtc);
    }

    [Fact]
    public void Persisted_trial_state_requires_current_schema_and_sane_clock_order()
    {
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.True(TrialStatePolicy.IsValidPersisted(TrialState.Create(start)));
        Assert.False(TrialStatePolicy.IsValidPersisted(TrialState.Create(start) with { SchemaVersion = 99 }));
        Assert.False(TrialStatePolicy.IsValidPersisted(TrialState.Create(start) with { LastSeenUtc = start.AddMinutes(-1) }));
        Assert.False(TrialStatePolicy.IsValidPersisted(new TrialState()));
    }

    [Fact]
    public void Pro_entitlement_overrides_expired_trial()
    {
        var now = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var snapshot = EntitlementSnapshot.Create(ProductTier.ProLifetime, now, null, true, "test");

        Assert.Equal(ProductTier.ProLifetime, snapshot.Tier);
        Assert.True(snapshot.CanUse(ProductFeature.CompareWorkspace));
    }
}

public sealed class AiCommerceTests
{
    [Fact]
    public void Plus_trial_never_unlocks_ai_features()
    {
        var aiFeatures = new[]
        {
            ProductFeature.AiProviders,
            ProductFeature.MagicActions,
            ProductFeature.ContextStack,
            ProductFeature.EvidenceAnchoring,
            ProductFeature.SemanticCompare,
            ProductFeature.CustomMagicActions,
            ProductFeature.CustomDestinations,
            ProductFeature.AiGuard,
            ProductFeature.AiResultCache,
            ProductFeature.MagicRecipes
        };

        foreach (var feature in aiFeatures)
        {
            Assert.False(FeatureCatalog.CanUse(ProductTier.Free, feature));
            Assert.False(FeatureCatalog.CanUse(ProductTier.PlusTrial, feature));
            Assert.True(FeatureCatalog.CanUse(ProductTier.ProLifetime, feature));
            Assert.Equal(ProductTier.ProLifetime, FeatureCatalog.RequiredTier(feature));
        }
    }
}
