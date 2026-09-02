using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiProviderRouterTests
{
    [Fact]
    public void Active_only_returns_active_compatible_model()
    {
        var active = Candidate("cloud", local: false, active: true, AiCapability.TextInput | AiCapability.VisionInput);
        var local = Candidate("local", local: true, active: false, AiCapability.TextInput);
        var action = BuiltInMagicActions.ById("general.summarize");

        var route = AiProviderRouter.Rank(action, [active, local], AiRoutingMode.ActiveOnly);

        Assert.Single(route);
        Assert.Equal("cloud", route[0].Id);
    }

    [Fact]
    public void Prefer_local_ranks_compatible_local_model_first()
    {
        var activeCloud = Candidate("cloud", local: false, active: true, AiCapability.TextInput | AiCapability.VisionInput);
        var local = Candidate("local", local: true, active: false, AiCapability.TextInput);
        var action = BuiltInMagicActions.ById("general.summarize");

        var route = AiProviderRouter.Rank(action, [activeCloud, local], AiRoutingMode.PreferLocal);

        Assert.Equal("local", route[0].Id);
    }

    [Fact]
    public void Required_vision_filters_text_only_models()
    {
        var textOnly = Candidate("small-local", local: true, active: true, AiCapability.TextInput);
        var vision = Candidate("vision", local: false, active: false, AiCapability.TextInput | AiCapability.VisionInput);
        var action = BuiltInMagicActions.ById("ui.ux-review");

        var route = AiProviderRouter.Rank(action, [textOnly, vision], AiRoutingMode.BestCapability);

        Assert.Single(route);
        Assert.Equal("vision", route[0].Id);
    }

    private static AiProviderCandidate Candidate(string id, bool local, bool active, AiCapability capabilities) =>
        new(id, new AiModelProfile(id, capabilities | (local ? AiCapability.LocalEndpoint : AiCapability.None), AiContextSizeClass.Medium,
            capabilities.HasFlag(AiCapability.VisionInput) ? AiVisionQuality.Strong : AiVisionQuality.None), active, local);
}
