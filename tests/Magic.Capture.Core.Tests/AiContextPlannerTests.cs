using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiContextPlannerTests
{
    [Fact]
    public void Text_only_model_never_receives_images()
    {
        var model = new AiModelProfile("tiny", AiCapability.TextInput | AiCapability.StructuredJson, AiContextSizeClass.Small, AiVisionQuality.None);
        var action = BuiltInMagicActions.ById("general.explain");
        var plan = AiContextPlanner.Plan(action, model, Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], AiPrivacyOptions.Default);
        Assert.False(plan.IncludePrimaryImage);
        Assert.Empty(plan.ContextImageIds);
    }

    [Fact]
    public void Basic_vision_receives_primary_but_not_stack_by_default()
    {
        var current = Guid.NewGuid();
        var model = new AiModelProfile("vision-small", AiCapability.TextInput | AiCapability.VisionInput, AiContextSizeClass.Medium, AiVisionQuality.Basic);
        var plan = AiContextPlanner.Plan(BuiltInMagicActions.ById("general.explain"), model, current, [Guid.NewGuid()], AiPrivacyOptions.Default);
        Assert.True(plan.IncludePrimaryImage);
        Assert.Empty(plan.ContextImageIds);
    }

    [Fact]
    public void Strong_vision_semantic_compare_routes_explicit_context_images()
    {
        var other = Guid.NewGuid();
        var model = new AiModelProfile("vision-strong", AiCapability.TextInput | AiCapability.VisionInput | AiCapability.MultipleImages, AiContextSizeClass.Large, AiVisionQuality.Strong);
        var plan = AiContextPlanner.Plan(BuiltInMagicActions.ById("compare.semantic"), model, Guid.NewGuid(), [other], AiPrivacyOptions.Default);
        Assert.True(plan.IncludePrimaryImage);
        Assert.Contains(other, plan.ContextImageIds);
    }

    [Fact]
    public void Never_send_images_policy_overrides_vision()
    {
        var model = new AiModelProfile("vision", AiCapability.TextInput | AiCapability.VisionInput, AiContextSizeClass.Large, AiVisionQuality.Strong);
        var policy = AiPrivacyOptions.Default with { NeverSendImagesToCloud = true, ProviderIsLocal = false };
        var plan = AiContextPlanner.Plan(BuiltInMagicActions.ById("general.explain"), model, Guid.NewGuid(), [], policy);
        Assert.False(plan.IncludePrimaryImage);
    }
}
