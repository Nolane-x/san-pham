namespace Magic.Capture.Core.Ai;

public sealed record AiPrivacyOptions(bool NeverSendImagesToCloud, bool LocalProvidersOnly, bool ProviderIsLocal, bool PreferTextOnlyWhenPossible)
{
    public static AiPrivacyOptions Default => new(false, false, false, true);
}

public sealed record AiPayloadSummary(bool UsesImages, int ImageCount, int ContextItemCount, bool UsesScreenGraph);

public sealed record AiContextPlan(bool IncludePrimaryImage, IReadOnlyList<Guid> ContextImageIds, AiPayloadSummary Summary);

public static class AiContextPlanner
{
    public static AiContextPlan Plan(MagicActionDefinition action, AiModelProfile model, Guid primaryCaptureId,
        IReadOnlyList<Guid> contextCaptureIds, AiPrivacyOptions privacy)
    {
        var canVision = model.Has(AiCapability.VisionInput) && model.VisionQuality != AiVisionQuality.None;
        var cloudImagesBlocked = privacy.NeverSendImagesToCloud && !privacy.ProviderIsLocal;
        var actionCanUseVision = action.VisionMode != MagicActionVisionMode.None;
        var includePrimary = canVision && actionCanUseVision && !cloudImagesBlocked && !
            (privacy.PreferTextOnlyWhenPossible && action.VisionMode == MagicActionVisionMode.Optional && model.ContextSize == AiContextSizeClass.Small);

        if (action.VisionMode == MagicActionVisionMode.Required && (!canVision || cloudImagesBlocked))
            includePrimary = false;

        var stack = new List<Guid>();
        var canMultiple = includePrimary && model.Has(AiCapability.MultipleImages) && model.VisionQuality == AiVisionQuality.Strong;
        if (canMultiple && action.SupportsContextStack)
            stack.AddRange(contextCaptureIds.Take(7));

        return new AiContextPlan(includePrimary, stack,
            new AiPayloadSummary(includePrimary || stack.Count > 0, (includePrimary ? 1 : 0) + stack.Count, contextCaptureIds.Count, true));
    }
}
