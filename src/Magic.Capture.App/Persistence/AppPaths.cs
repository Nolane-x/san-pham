namespace Magic.Capture.App.Persistence;

internal sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Magic Capture Desktop");
        HistoryRoot = Path.Combine(Root, "history");
        SettingsFile = Path.Combine(Root, "settings.json");
        TrialStateFile = Path.Combine(Root, "trial.json");
        EntitlementCacheFile = Path.Combine(Root, "entitlement-cache.json");
        AiProvidersFile = Path.Combine(Root, "ai-providers.json");
        MagicActionsFile = Path.Combine(Root, "magic-actions.json");
        WorkflowsFile = Path.Combine(Root, "workflows.json");
        WorkflowTracesFile = Path.Combine(Root, "workflow-traces.json");
        WorkflowTriggersFile = Path.Combine(Root, "workflow-triggers.json");
        WorkflowTriggerHistoryFile = Path.Combine(Root, "workflow-trigger-history.json");
        DestinationsFile = Path.Combine(Root, "destinations.json");
        LocalActionsFile = Path.Combine(Root, "local-actions.json");
        LocalActionApprovalsFile = Path.Combine(Root, "local-action-approvals.json");
        LocalActionTempRoot = Path.Combine(Root, "local-action-temp");
        AiRecipesFile = Path.Combine(Root, "magic-recipes.json");
        AiCacheRoot = Path.Combine(Root, "ai-cache");
        HistoryIndexFile = Path.Combine(HistoryRoot, "index.json");
        HistoryLibraryFile = Path.Combine(HistoryRoot, "history-library.json");
        HistoryPendingAddFile = Path.Combine(HistoryRoot, "pending-add.json");
        HistoryIconCacheRoot = Path.Combine(HistoryRoot, "icons");
        LogsRoot = Path.Combine(Root, "logs");
        RecordingJournalFile = Path.Combine(Root, "recording-session.json");
        VideoEditOverlayCacheRoot = Path.Combine(Root, "video-edit-overlay-cache");
        EditableProjectRecoveryRoot = Path.Combine(Root, "recovery", "editable-projects");
        DocumentationRecoveryRoot = Path.Combine(Root, "recovery", "documentation");
        VideoEditRecoveryRoot = Path.Combine(Root, "recovery", "video-edit");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(HistoryRoot);
        Directory.CreateDirectory(HistoryIconCacheRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(AiCacheRoot);
        Directory.CreateDirectory(LocalActionTempRoot);
        Directory.CreateDirectory(VideoEditOverlayCacheRoot);
        Directory.CreateDirectory(EditableProjectRecoveryRoot);
        Directory.CreateDirectory(DocumentationRecoveryRoot);
        Directory.CreateDirectory(VideoEditRecoveryRoot);
    }

    public string Root { get; }
    public string HistoryRoot { get; }
    public string SettingsFile { get; }
    public string TrialStateFile { get; }
    public string EntitlementCacheFile { get; }
    public string AiProvidersFile { get; }
    public string MagicActionsFile { get; }
    public string WorkflowsFile { get; }
    public string WorkflowTracesFile { get; }
    public string WorkflowTriggersFile { get; }
    public string WorkflowTriggerHistoryFile { get; }
    public string DestinationsFile { get; }
    public string LocalActionsFile { get; }
    public string LocalActionApprovalsFile { get; }
    public string LocalActionTempRoot { get; }
    public string AiRecipesFile { get; }
    public string AiCacheRoot { get; }
    public string HistoryIndexFile { get; }
    public string HistoryLibraryFile { get; }
    public string HistoryPendingAddFile { get; }
    public string HistoryIconCacheRoot { get; }
    public string LogsRoot { get; }
    public string RecordingJournalFile { get; }
    public string VideoEditOverlayCacheRoot { get; }
    public string EditableProjectRecoveryRoot { get; }
    public string DocumentationRecoveryRoot { get; }
    public string VideoEditRecoveryRoot { get; }
}
