namespace Magic.Capture.Core.Commerce;

public static class TrialStatePolicy
{
    public static bool IsValidPersisted(TrialState? state)
    {
        if (state is null) return false;
        if (state.SchemaVersion != TrialState.CurrentSchemaVersion) return false;
        if (state.StartedUtc == default || state.LastSeenUtc == default) return false;
        if (state.LastSeenUtc < state.StartedUtc) return false;
        return true;
    }
}
