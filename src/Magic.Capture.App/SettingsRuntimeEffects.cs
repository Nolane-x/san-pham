namespace Magic.Capture.App;

[Flags]
internal enum SettingsRuntimeEffects
{
    None = 0,
    MainWindowUi = 1,
    Theme = 2,
    HistoryRetention = 4,
    WorkflowTriggers = 8,
    All = MainWindowUi | Theme | HistoryRetention | WorkflowTriggers
}
