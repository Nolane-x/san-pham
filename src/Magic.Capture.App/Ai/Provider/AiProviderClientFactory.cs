namespace Magic.Capture.App.Ai.Provider;

internal sealed class AiProviderClientFactory
{
    private readonly IAiSecretStore _secrets;
    public AiProviderClientFactory(IAiSecretStore secrets) => _secrets = secrets;

    public IAiProviderClient Create(AiProviderProfile profile) => profile.Kind switch
    {
        AiProviderKind.OpenAI => new OpenAiResponsesClient(profile, _secrets),
        AiProviderKind.Anthropic => new AnthropicMessagesClient(profile, _secrets),
        AiProviderKind.Gemini => new GeminiClient(profile, _secrets),
        AiProviderKind.Ollama => new OllamaClient(profile, _secrets),
        _ => new OpenAiCompatibleClient(profile, _secrets)
    };
}
