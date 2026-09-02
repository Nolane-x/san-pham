namespace Magic.Capture.Core.Ai;

public enum AiRoutingMode
{
    ActiveOnly,
    PreferLocal,
    BestCapability
}

public sealed record AiProviderCandidate(
    string Id,
    AiModelProfile Model,
    bool IsActive,
    bool IsLocal);

public static class AiProviderRouter
{
    public static IReadOnlyList<AiProviderCandidate> Rank(
        MagicActionDefinition action,
        IEnumerable<AiProviderCandidate> candidates,
        AiRoutingMode mode)
    {
        var compatible = candidates
            .Where(c => (c.Model.Capabilities & action.MinimumCapabilities) == action.MinimumCapabilities)
            .Where(c => action.VisionMode != MagicActionVisionMode.Required || c.Model.Has(AiCapability.VisionInput))
            .ToArray();

        if (mode == AiRoutingMode.ActiveOnly)
            return compatible.Where(c => c.IsActive).Take(1).ToArray();

        static int CapabilityScore(AiProviderCandidate c, MagicActionDefinition action)
        {
            var preferred = c.Model.Capabilities & action.PreferredCapabilities;
            var score = CountBits((int)preferred) * 8;
            score += c.Model.ContextSize switch { AiContextSizeClass.Large => 8, AiContextSizeClass.Medium => 4, _ => 0 };
            score += c.Model.VisionQuality switch { AiVisionQuality.Strong => 8, AiVisionQuality.Basic => 3, _ => 0 };
            if (c.Model.Has(AiCapability.StructuredJson)) score += 2;
            if (c.Model.Has(AiCapability.JsonSchema)) score += 2;
            return score;
        }

        return compatible
            .OrderByDescending(c => mode == AiRoutingMode.PreferLocal && c.IsLocal)
            .ThenByDescending(c => CapabilityScore(c, action))
            .ThenByDescending(c => c.IsActive)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static int CountBits(int value)
    {
        var count = 0;
        var unsigned = unchecked((uint)value);
        while (unsigned != 0)
        {
            count += (int)(unsigned & 1);
            unsigned >>= 1;
        }
        return count;
    }
}
