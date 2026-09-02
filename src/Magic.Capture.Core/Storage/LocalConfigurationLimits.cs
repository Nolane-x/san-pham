namespace Magic.Capture.Core.Storage;

public static class LocalConfigurationLimits
{
    public const int MaximumCustomWorkflows = 128;
    public const int MaximumDestinations = 64;
    public const int MaximumMagicActions = 256;
    public const int MaximumMagicRecipes = 128;
    public const int MaximumAiProviderProfiles = 32;
    public const int MaximumLocalActions = 128;
    public const int MaximumLocalActionApprovals = 256;

    public const long MaximumSettingsJsonBytes = 2L * 1024 * 1024;
    public const long MaximumWorkflowJsonBytes = 2L * 1024 * 1024;
    public const long MaximumWorkflowTraceJsonBytes = 2L * 1024 * 1024;
    public const long MaximumWorkflowTriggerJsonBytes = 1L * 1024 * 1024;
    public const long MaximumWorkflowTriggerHistoryJsonBytes = 1L * 1024 * 1024;
    public const long MaximumDestinationJsonBytes = 2L * 1024 * 1024;
    public const long MaximumMagicActionJsonBytes = 4L * 1024 * 1024;
    public const long MaximumMagicRecipeJsonBytes = 2L * 1024 * 1024;
    public const long MaximumAiProviderJsonBytes = 1L * 1024 * 1024;
    public const long MaximumLocalActionJsonBytes = 2L * 1024 * 1024;
    public const long MaximumLocalActionApprovalJsonBytes = 512L * 1024;

    public static void ValidateCount(int count, int maximum, string label)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (count > maximum)
            throw new InvalidDataException($"{label} contains {count:N0} items, above the supported limit of {maximum:N0}.");
    }
}
