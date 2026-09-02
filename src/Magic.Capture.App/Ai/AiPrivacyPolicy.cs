using Magic.Capture.App.Ai.Provider;
using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai;

internal static class AiPrivacyPolicy
{
    public static AiPrivacyOptions ToCore(AiPrivacySettings settings, AiProviderProfile profile) =>
        new(settings.NeverSendImagesToCloud, settings.LocalProvidersOnly, profile.IsLocal, settings.PreferTextOnlyWhenPossible);

    public static void Validate(AiPrivacySettings settings, AiProviderProfile profile)
    {
        if (settings.LocalProvidersOnly && !profile.IsLocal)
            throw new InvalidOperationException("AI privacy is set to local providers only. Choose Ollama, LM Studio, or another localhost endpoint.");
    }
}
