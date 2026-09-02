using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai.Provider;

internal enum AiProviderKind
{
    OpenAI,
    Anthropic,
    Gemini,
    OpenRouter,
    OpenAiCompatible,
    Ollama,
    LmStudio
}

internal sealed record AiProviderProfile(
    Guid Id,
    string DisplayName,
    AiProviderKind Kind,
    string BaseUri,
    string ModelId,
    AiCapability Capabilities,
    AiContextSizeClass ContextSize,
    AiVisionQuality VisionQuality,
    string SecretId,
    bool Enabled = true,
    int TimeoutSeconds = 90)
{
    public bool IsLocal => Uri.TryCreate(BaseUri, UriKind.Absolute, out var uri) &&
        (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    public AiModelProfile ToModelProfile() => new(ModelId, Capabilities | (IsLocal ? AiCapability.LocalEndpoint : AiCapability.None), ContextSize, VisionQuality);

    public override string ToString() => $"{DisplayName} ({Kind}, {ModelId})";
}

internal sealed record AiPrivacySettings(
    bool PreferTextOnlyWhenPossible = true,
    bool NeverSendImagesToCloud = false,
    bool LocalProvidersOnly = false,
    bool ShowPayloadSummaryBeforeCloudAction = true,
    AiRoutingMode RoutingMode = AiRoutingMode.ActiveOnly);

internal sealed record AiProviderProfileState(
    IReadOnlyList<AiProviderProfile> Profiles,
    Guid? ActiveProfileId,
    AiPrivacySettings Privacy)
{
    public static AiProviderProfileState Empty => new([], null, new AiPrivacySettings());
}
