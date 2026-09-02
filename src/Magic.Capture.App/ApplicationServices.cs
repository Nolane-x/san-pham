using Magic.Capture.App.Analysis;
using Magic.Capture.App.Ai;
using Magic.Capture.App.Ai.Provider;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Commerce;
using Magic.Capture.App.Export;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Utilities;
using Magic.Capture.App.Workflows;
using Magic.Capture.App.Destinations;
using Magic.Capture.App.Documentation;
using Magic.Capture.App.LocalActions;
using Magic.Capture.App.Persistence;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Privacy;
using Magic.Capture.App.Recording;
using Magic.Capture.App.VideoEditing;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.App;

internal sealed class ApplicationServices
{
    public required AppPaths Paths { get; init; }
    public required LocalLog Log { get; init; }
    public required SettingsStore SettingsStore { get; init; }
    public required HistoryStore HistoryStore { get; init; }
    public required HistoryLibraryStore HistoryLibrary { get; init; }
    public required HistoryProcessIconCache HistoryProcessIcons { get; init; }
    public required ConfigurationArchiveService ConfigurationArchive { get; init; }
    public required HistoryArchiveService HistoryArchive { get; init; }
    public required EditableProjectService EditableProjects { get; init; }
    public required EditableProjectRecoveryStore EditableProjectRecovery { get; init; }
    public required ClipboardService Clipboard { get; init; }
    public required ExportService Export { get; init; }
    public required AnalysisService Analysis { get; init; }
    public required ITextRecognitionService Ocr { get; init; }
    public required BarcodeService Barcode { get; init; }
    public required CaptureCoordinator Capture { get; init; }
    public required CaptureWatchService CaptureWatch { get; init; }
    public required AutomaticScrollCaptureService AutomaticScroll { get; init; }
    public required TwoDimensionalScrollCaptureService TwoDimensionalScroll { get; init; }
    public required ScreenCaptureService ScreenCapture { get; init; }
    public required WindowCaptureService WindowCapture { get; init; }
    public required UiAutomationSnapshotService UiAutomation { get; init; }
    public required MonitorService Monitors { get; init; }
    public required ImageTransformService Transforms { get; init; }
    public required AnnotationRenderer AnnotationRenderer { get; init; }
    public required CaptureRedactionService Redaction { get; init; }
    public required VerticalImageStitcher Stitcher { get; init; }
    public required HorizontalImageStitcher HorizontalStitcher { get; init; }
    public required GridImageStitcher GridStitcher { get; init; }
    public required NativeMessageRouter MessageRouter { get; init; }
    public required HotkeyService Hotkeys { get; init; }
    public required TrayIconService Tray { get; init; }
    public required StartupService Startup { get; init; }
    public required EntitlementService Entitlements { get; init; }
    public required StorePurchaseService StorePurchase { get; init; }
    public required ScreenGraphService ScreenGraph { get; init; }
    public required AiProviderProfileStore AiProfiles { get; init; }
    public required IAiSecretStore AiSecrets { get; init; }
    public required AiProviderClientFactory AiClients { get; init; }
    public required MagicActionStore MagicActionStore { get; init; }
    public required MagicRecipeStore MagicRecipeStore { get; init; }
    public MagicRecipeService? MagicRecipes { get; set; }
    public required AiImagePreprocessor AiImagePreprocessor { get; init; }
    public required MagicActionService MagicActions { get; init; }
    public required AiContextStackService AiContext { get; init; }
    public required AiResultCache AiCache { get; init; }
    public required ImageUtilityService ImageUtilities { get; init; }
    public required ImageOptimizationService ImageOptimization { get; init; }
    public required PdfExportService PdfExport { get; init; }
    public required BarcodeGeneratorService BarcodeGenerator { get; init; }
    public required ImagePixelStatisticsService PixelStatistics { get; init; }
    public required ImageEffectPipelineService ImageEffects { get; init; }
    public required ImageCanvasOperationsService ImageCanvasOperations { get; init; }
    public required MetadataService Metadata { get; init; }
    public required WorkflowStore Workflows { get; init; }
    public required WorkflowExecutor WorkflowExecutor { get; init; }
    public required WorkflowBatchRunner WorkflowBatchRunner { get; init; }
    public required WorkflowTraceStore WorkflowTraces { get; init; }
    public required WorkflowTriggerStore WorkflowTriggers { get; init; }
    public required WorkflowTriggerHistoryStore WorkflowTriggerHistory { get; init; }
    public required WorkflowTriggerRunner WorkflowTriggerRunner { get; init; }
    public required ResidentWorkflowTriggerEngine WorkflowTriggerEngine { get; init; }
    public required WindowsTaskSchedulerService WorkflowTaskScheduler { get; init; }
    public required DestinationProfileStore Destinations { get; init; }
    public required IDestinationSecretStore DestinationSecrets { get; init; }
    public required CustomHttpDestinationClient DestinationClient { get; init; }
    public required LocalActionProfileStore LocalActions { get; init; }
    public required LocalActionApprovalStore LocalActionApprovals { get; init; }
    public required LocalActionRunner LocalActionRunner { get; init; }
    public required AudioDeviceCatalog RecordingAudioDevices { get; init; }
    public required RecordingSessionService Recording { get; init; }
    public required StepRecorderService StepRecorder { get; init; }
    public required DocumentationProjectStore DocumentationProjects { get; init; }
    public required DocumentationRecoveryStore DocumentationRecovery { get; init; }
    public required DocumentationCardRenderer DocumentationRenderer { get; init; }
    public required DocumentationExportService DocumentationExport { get; init; }
    public required VideoEditProjectStore VideoEditProjects { get; init; }
    public required VideoEditRecoveryStore VideoEditRecovery { get; init; }
    public required VideoEditCompositionService VideoEditComposition { get; init; }
    public required VideoEditThumbnailService VideoEditThumbnails { get; init; }
    public required VideoEditTrackingService VideoEditTracking { get; init; }
    public required VideoEditTranscodeService VideoEditTranscode { get; init; }
    public required VideoEditAdvancedRenderService VideoEditAdvancedRender { get; init; }
    private AppSettings _settings = AppSettingsRules.NormalizeForRuntime(new AppSettings());
    public AppSettings Settings => _settings;

    internal void CommitSettingsSnapshot(AppSettings settings) =>
        _settings = AppSettingsRules.NormalizeForRuntime(settings);
}
