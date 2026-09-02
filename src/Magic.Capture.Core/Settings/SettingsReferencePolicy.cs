namespace Magic.Capture.Core.Settings;

public static class SettingsReferencePolicy
{
    public static AppSettings RemoveWorkflowReferences(AppSettings settings, string workflowId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        return AppSettingsRules.NormalizeForRuntime(settings with
        {
            DefaultWorkflowId = EqualsId(settings.DefaultWorkflowId, workflowId) ? null : settings.DefaultWorkflowId,
            CaptureProfiles = settings.CaptureProfiles.Select(profile =>
                EqualsId(profile.WorkflowId, workflowId) ? profile with { WorkflowId = null } : profile).ToArray(),
            PersonalHotkeys = settings.PersonalHotkeys.Where(binding =>
                binding.Kind != PersonalHotkeyKind.Workflow || !EqualsId(binding.Target, workflowId)).ToArray()
        });
    }

    public static AppSettings RemoveMagicActionReferences(AppSettings settings, string actionId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return AppSettingsRules.NormalizeForRuntime(settings with
        {
            PersonalHotkeys = settings.PersonalHotkeys.Where(binding =>
                binding.Kind != PersonalHotkeyKind.MagicAction || !EqualsId(binding.Target, actionId)).ToArray()
        });
    }

    public static AppSettings RemoveCaptureProfileReferences(AppSettings settings, string profileId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profileTarget = "profile:" + profileId;
        return AppSettingsRules.NormalizeForRuntime(settings with
        {
            CaptureProfiles = settings.CaptureProfiles.Where(profile => !EqualsId(profile.Id, profileId)).ToArray(),
            DefaultCaptureProfileId = EqualsId(settings.DefaultCaptureProfileId, profileId) ? null : settings.DefaultCaptureProfileId,
            AppCaptureRules = settings.AppCaptureRules.Where(rule => !EqualsId(rule.CaptureProfileId, profileId)).ToArray(),
            PersonalHotkeys = settings.PersonalHotkeys.Where(binding =>
                binding.Kind != PersonalHotkeyKind.Capture || !EqualsId(binding.Target, profileTarget)).ToArray()
        });
    }

    public static AppSettings PruneExternalReferences(
        AppSettings settings,
        IReadOnlySet<string> validWorkflowIds,
        IReadOnlySet<string> validMagicActionIds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(validWorkflowIds);
        ArgumentNullException.ThrowIfNull(validMagicActionIds);
        var profileIds = settings.CaptureProfiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        var pruned = settings with
        {
            DefaultWorkflowId = IsValid(settings.DefaultWorkflowId, validWorkflowIds) ? settings.DefaultWorkflowId : null,
            CaptureProfiles = settings.CaptureProfiles.Select(profile =>
                IsValid(profile.WorkflowId, validWorkflowIds) ? profile : profile with { WorkflowId = null }).ToArray(),
            PersonalHotkeys = settings.PersonalHotkeys.Where(binding => binding.Kind switch
            {
                PersonalHotkeyKind.Workflow => validWorkflowIds.Contains(binding.Target),
                PersonalHotkeyKind.MagicAction => validMagicActionIds.Contains(binding.Target),
                PersonalHotkeyKind.Capture when binding.Target.StartsWith("profile:", StringComparison.OrdinalIgnoreCase) =>
                    profileIds.Contains(binding.Target["profile:".Length..]),
                _ => true
            }).ToArray()
        };
        return AppSettingsRules.NormalizeForRuntime(pruned);
    }

    public static bool RequiresExternalReferencePrune(
        AppSettings settings,
        IReadOnlySet<string> validWorkflowIds,
        IReadOnlySet<string> validMagicActionIds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(validWorkflowIds);
        ArgumentNullException.ThrowIfNull(validMagicActionIds);
        if (!IsValid(settings.DefaultWorkflowId, validWorkflowIds)) return true;
        if (settings.CaptureProfiles.Any(profile => !IsValid(profile.WorkflowId, validWorkflowIds))) return true;
        var profileIds = settings.CaptureProfiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        return settings.PersonalHotkeys.Any(binding => binding.Kind switch
        {
            PersonalHotkeyKind.Workflow => !validWorkflowIds.Contains(binding.Target),
            PersonalHotkeyKind.MagicAction => !validMagicActionIds.Contains(binding.Target),
            PersonalHotkeyKind.Capture when binding.Target.StartsWith("profile:", StringComparison.OrdinalIgnoreCase) =>
                !profileIds.Contains(binding.Target["profile:".Length..]),
            _ => false
        });
    }

    private static bool IsValid(string? value, IReadOnlySet<string> valid) => string.IsNullOrWhiteSpace(value) || valid.Contains(value);
    private static bool EqualsId(string? left, string right) => string.Equals(left, right, StringComparison.Ordinal);
}
