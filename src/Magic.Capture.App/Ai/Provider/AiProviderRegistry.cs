using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai.Provider;

internal static class AiProviderRegistry
{
    public static AiProviderProfile CreateDefault(AiProviderKind kind) => kind switch
    {
        AiProviderKind.OpenAI => P(kind, "OpenAI", "https://api.openai.com", "gpt-5.6", AiCapability.TextInput | AiCapability.VisionInput | AiCapability.MultipleImages | AiCapability.StructuredJson, AiVisionQuality.Strong),
        AiProviderKind.Anthropic => P(kind, "Anthropic", "https://api.anthropic.com", "claude-sonnet-4-5", AiCapability.TextInput | AiCapability.VisionInput | AiCapability.MultipleImages | AiCapability.StructuredJson, AiVisionQuality.Strong),
        AiProviderKind.Gemini => P(kind, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash", AiCapability.TextInput | AiCapability.VisionInput | AiCapability.MultipleImages | AiCapability.StructuredJson, AiVisionQuality.Strong),
        AiProviderKind.OpenRouter => P(kind, "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-5.6", AiCapability.TextInput | AiCapability.VisionInput | AiCapability.MultipleImages | AiCapability.StructuredJson, AiVisionQuality.Strong),
        AiProviderKind.Ollama => P(kind, "Ollama", "http://localhost:11434", "gemma3", AiCapability.TextInput | AiCapability.VisionInput | AiCapability.StructuredJson | AiCapability.LocalEndpoint, AiVisionQuality.Basic),
        AiProviderKind.LmStudio => P(kind, "LM Studio", "http://localhost:1234/v1", "local-model", AiCapability.TextInput | AiCapability.StructuredJson | AiCapability.LocalEndpoint, AiVisionQuality.None),
        _ => P(kind, "OpenAI-compatible", "http://localhost:8000/v1", "model", AiCapability.TextInput | AiCapability.StructuredJson, AiVisionQuality.None)
    };

    private static AiProviderProfile P(AiProviderKind kind, string name, string uri, string model, AiCapability capabilities, AiVisionQuality vision) =>
        new(Guid.NewGuid(), name, kind, uri, model, capabilities, AiContextSizeClass.Medium, vision, $"ai-{Guid.NewGuid():N}");
}
