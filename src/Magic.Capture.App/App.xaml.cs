using Magic.Capture.Core.Platform;
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
using Magic.Capture.App.Views;
using Magic.Capture.Core.Color;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Cli;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Workflows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Activation;

namespace Magic.Capture.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private ApplicationServices? _services;
    private readonly List<Window> _childWindows = [];
    private bool _exitRequested;
    private bool _startupActivation;
    private SingleInstanceService? _singleInstance;
    private string[] _launchCliArgs = [];
    private readonly SemaphoreSlim _settingsMutationGate = new(1, 1);
    private long _settingsRevision;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    internal ApplicationServices Services => _services ?? throw new InvalidOperationException("Application services are not initialized.");
    internal bool IsExitRequested => _exitRequested;

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var activation = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
        _startupActivation = activation?.Kind == ActivationKind.StartupTask;
        _launchCliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            if (!_startupActivation)
            {
                if (_launchCliArgs.Length > 0) SingleInstanceService.SendCommand(_launchCliArgs);
                else SingleInstanceService.SignalPrimary();
            }
            Exit();
            return;
        }

        _mainWindow = new MainWindow();
        var hwnd = WindowHelpers.GetWindowHandle(_mainWindow);
        var router = new NativeMessageRouter();
        router.Attach(hwnd);

        var paths = new AppPaths();
        var log = new LocalLog(paths);
        var settingsStore = new SettingsStore(paths);
        var historyLibrary = new HistoryLibraryStore(paths, log);
        var historyProcessIcons = new HistoryProcessIconCache(paths);
        var history = new HistoryStore(paths, historyLibrary);
        var configurationArchive = new ConfigurationArchiveService(paths);
        var historyArchive = new HistoryArchiveService(history);
        var editableProjects = new EditableProjectService();
        var editableProjectRecovery = new EditableProjectRecoveryStore(paths, editableProjects, log);
        var monitors = new MonitorService();
        var d3dCaptureDevice = new Direct3D11DeviceHost();
        var wgcCapture = new WindowsGraphicsCaptureBackend(d3dCaptureDevice);
        var ddaCapture = new DesktopDuplicationCaptureBackend();
        var gdiCapture = new GdiCaptureBackend();
        var captureBackendRouter = new CaptureBackendRouter(monitors, [wgcCapture, ddaCapture, gdiCapture], log);
        var screen = new ScreenCaptureService(captureBackendRouter);
        var clipboard = new ClipboardService();
        var export = new ExportService();
        var ocr = new WindowsOcrService();
        var barcode = new BarcodeService();
        var analysis = new AnalysisService(ocr, barcode);
        var windowCapture = new WindowCaptureService(screen, monitors);
        var uiAutomation = new UiAutomationSnapshotService();
        var coordinator = new CaptureCoordinator(monitors, screen, windowCapture, uiAutomation);
        var captureWatch = new CaptureWatchService(coordinator);
        var transforms = new ImageTransformService();
        var annotationRenderer = new AnnotationRenderer();
        var stitcher = new VerticalImageStitcher();
        var horizontalStitcher = new HorizontalImageStitcher();
        var gridStitcher = new GridImageStitcher(horizontalStitcher, stitcher);
        var inputSynthesis = new InputSynthesisService();
        var automaticScroll = new AutomaticScrollCaptureService(screen, stitcher, horizontalStitcher, inputSynthesis);
        var twoDimensionalScroll = new TwoDimensionalScrollCaptureService(screen, gridStitcher, inputSynthesis);
        var hotkeys = new HotkeyService(hwnd, router);
        var tray = new TrayIconService(hwnd, router);
        var startup = new StartupService();
        var trialStore = new TrialStateStore(paths);
        var storePurchase = new StorePurchaseService(paths, log);
        var entitlements = new EntitlementService(trialStore, storePurchase);
        var screenGraph = new ScreenGraphService(analysis);
        var redaction = new CaptureRedactionService(screenGraph, annotationRenderer);
        var aiProfiles = new AiProviderProfileStore(paths);
        var aiSecrets = new WindowsPasswordVaultSecretStore();
        var aiClients = new AiProviderClientFactory(aiSecrets);
        var magicActionStore = new MagicActionStore(paths);
        var magicRecipeStore = new MagicRecipeStore(paths);
        var aiContext = new AiContextStackService();
        var aiImagePreprocessor = new AiImagePreprocessor(transforms);
        var aiCache = new AiResultCache(paths, log);
        var imageUtilities = new ImageUtilityService();
        var imageOptimization = new ImageOptimizationService();
        var pdfExport = new PdfExportService(imageUtilities);
        var barcodeGenerator = new BarcodeGeneratorService();
        var pixelStatistics = new ImagePixelStatisticsService();
        var imageEffects = new ImageEffectPipelineService();
        var imageCanvasOperations = new ImageCanvasOperationsService();
        var metadata = new MetadataService();
        var workflows = new WorkflowStore(paths);
        var destinationSecrets = new WindowsDestinationSecretStore();
        var destinations = new DestinationProfileStore(paths);
        var destinationClient = new CustomHttpDestinationClient(destinationSecrets);
        var localActions = new LocalActionProfileStore(paths);
        var localActionApprovals = new LocalActionApprovalStore(paths);
        var localActionRunner = new LocalActionRunner(paths, localActionApprovals);
        var recordingRecovery = new RecordingRecoveryStore(paths);
        var recordingFrames = new RecordingFrameProvider(screen, monitors);
        var recordingAudioDevices = new AudioDeviceCatalog();
        var recording = new RecordingSessionService(recordingFrames, recordingRecovery, log);
        var stepRecorder = new StepRecorderService(monitors, windowCapture, uiAutomation, screen, log);
        var documentationProjects = new DocumentationProjectStore();
        var documentationRecovery = new DocumentationRecoveryStore(paths, documentationProjects, log);
        var documentationRenderer = new DocumentationCardRenderer();
        var documentationExport = new DocumentationExportService(documentationRenderer, pdfExport);
        var videoEditProjects = new VideoEditProjectStore();
        var videoEditRecovery = new VideoEditRecoveryStore(paths, videoEditProjects, log);
        var videoEditOverlayAssets = new VideoEditOverlayAssetStore(paths, log);
        var videoEditComposition = new VideoEditCompositionService(log, videoEditOverlayAssets);
        var videoEditThumbnails = new VideoEditThumbnailService(videoEditComposition);
        var videoEditTracking = new VideoEditTrackingService(videoEditComposition, videoEditThumbnails);
        var videoEditTranscode = new VideoEditTranscodeService(videoEditComposition, log);
        var videoEditAdvancedRender = new VideoEditAdvancedRenderService(videoEditComposition, videoEditThumbnails, log);
        var magicActions = new MagicActionService(screenGraph, aiProfiles, aiClients, entitlements, () => _services!.Settings, aiImagePreprocessor, aiCache);
        var workflowExecutor = new WorkflowExecutor(clipboard, ocr, barcode, imageUtilities, metadata, magicActions, magicActionStore, entitlements, () => _services!.Settings, destinations, destinationClient, localActions, localActionRunner);
        var workflowTraces = new WorkflowTraceStore(paths);
        var workflowBatchRunner = new WorkflowBatchRunner(workflowExecutor, workflowTraces, log, historyLibrary);
        var workflowTriggers = new WorkflowTriggerStore(paths);
        var workflowTriggerHistory = new WorkflowTriggerHistoryStore(paths);
        var workflowTriggerHotkeys = new WorkflowTriggerHotkeyService(hwnd, router);
        var workflowTaskScheduler = new WindowsTaskSchedulerService();
        var workflowTriggerRunner = new WorkflowTriggerRunner(
            workflowTriggers, workflowTriggerHistory, workflows, entitlements, () => _services!.Settings,
            RunCaptureProfileForAutomationAsync, log);
        var workflowTriggerEngine = new ResidentWorkflowTriggerEngine(
            hwnd, router, workflowTriggers, workflowTriggerRunner, workflowTriggerHotkeys, entitlements, log);

        _services = new ApplicationServices
        {
            Paths = paths,
            Log = log,
            SettingsStore = settingsStore,
            HistoryStore = history,
            HistoryLibrary = historyLibrary,
            HistoryProcessIcons = historyProcessIcons,
            ConfigurationArchive = configurationArchive,
            HistoryArchive = historyArchive,
            EditableProjects = editableProjects,
            EditableProjectRecovery = editableProjectRecovery,
            Clipboard = clipboard,
            Export = export,
            Ocr = ocr,
            Barcode = barcode,
            Analysis = analysis,
            Capture = coordinator,
            CaptureWatch = captureWatch,
            AutomaticScroll = automaticScroll,
            TwoDimensionalScroll = twoDimensionalScroll,
            ScreenCapture = screen,
            WindowCapture = windowCapture,
            UiAutomation = uiAutomation,
            Monitors = monitors,
            Transforms = transforms,
            AnnotationRenderer = annotationRenderer,
            Redaction = redaction,
            Stitcher = stitcher,
            HorizontalStitcher = horizontalStitcher,
            GridStitcher = gridStitcher,
            MessageRouter = router,
            Hotkeys = hotkeys,
            Tray = tray,
            Startup = startup,
            StorePurchase = storePurchase,
            Entitlements = entitlements,
            ScreenGraph = screenGraph,
            AiProfiles = aiProfiles,
            AiSecrets = aiSecrets,
            AiClients = aiClients,
            MagicActionStore = magicActionStore,
            MagicRecipeStore = magicRecipeStore,
            AiContext = aiContext,
            AiImagePreprocessor = aiImagePreprocessor,
            AiCache = aiCache,
            ImageUtilities = imageUtilities,
            ImageOptimization = imageOptimization,
            PdfExport = pdfExport,
            BarcodeGenerator = barcodeGenerator,
            PixelStatistics = pixelStatistics,
            ImageEffects = imageEffects,
            ImageCanvasOperations = imageCanvasOperations,
            Metadata = metadata,
            Workflows = workflows,
            WorkflowExecutor = workflowExecutor,
            WorkflowBatchRunner = workflowBatchRunner,
            WorkflowTraces = workflowTraces,
            WorkflowTriggers = workflowTriggers,
            WorkflowTriggerHistory = workflowTriggerHistory,
            WorkflowTriggerRunner = workflowTriggerRunner,
            WorkflowTriggerEngine = workflowTriggerEngine,
            WorkflowTaskScheduler = workflowTaskScheduler,
            Destinations = destinations,
            DestinationSecrets = destinationSecrets,
            DestinationClient = destinationClient,
            LocalActions = localActions,
            LocalActionApprovals = localActionApprovals,
            LocalActionRunner = localActionRunner,
            RecordingAudioDevices = recordingAudioDevices,
            Recording = recording,
            StepRecorder = stepRecorder,
            DocumentationProjects = documentationProjects,
            DocumentationRecovery = documentationRecovery,
            DocumentationRenderer = documentationRenderer,
            DocumentationExport = documentationExport,
            VideoEditProjects = videoEditProjects,
            VideoEditRecovery = videoEditRecovery,
            VideoEditComposition = videoEditComposition,
            VideoEditThumbnails = videoEditThumbnails,
            VideoEditTracking = videoEditTracking,
            VideoEditTranscode = videoEditTranscode,
            VideoEditAdvancedRender = videoEditAdvancedRender,
            MagicActions = magicActions,
        };
        _services.MagicRecipes = new MagicRecipeService(workflowExecutor, () => entitlements.Current.Tier);

        WireResidentEvents();
        entitlements.Changed += Entitlements_Changed;
        _mainWindow.AttachServices(_services);
        _singleInstance.StartListening(
            () => _mainWindow.DispatcherQueue.TryEnqueue(ShowMainWindow),
            commandArgs => _mainWindow.DispatcherQueue.TryEnqueue(async () => await HandleCliArgsAsync(commandArgs)));
        tray.Add();
        if (!_startupActivation && _launchCliArgs.Length == 0) _mainWindow.Activate();
        _ = InitializeAsync(hwnd);
    }

    private async Task InitializeAsync(IntPtr hwnd)
    {
        var warnings = new List<string>();
        try
        {
            SettingsLoadResult settingsLoad;
            try
            {
                settingsLoad = await Services.SettingsStore.LoadAsync();
            }
            catch (Exception ex)
            {
                Services.Log.Error("SettingsInitialize", ex);
                settingsLoad = new SettingsLoadResult(
                    AppSettingsRules.NormalizeForRuntime(new AppSettings()),
                    true,
                    "Settings initialization failed unexpectedly. Safe defaults are being used for this session.");
            }

            Services.CommitSettingsSnapshot(settingsLoad.Settings);
            ApplyTheme(_mainWindow, Services.Settings.Theme);
            if (settingsLoad.UsedFallback && !string.IsNullOrWhiteSpace(settingsLoad.Warning))
                warnings.Add(settingsLoad.Warning);

            try
            {
                await Services.Entitlements.InitializeAsync(hwnd);
            }
            catch (Exception ex)
            {
                // EntitlementSnapshot starts at Free. Keep that fail-safe state rather than making
                // the rest of the resident app initialization depend on Store/trial persistence.
                Services.Log.Error("EntitlementInitialize", ex);
                warnings.Add("Licensing initialization failed. Magic Capture Desktop is staying in Free mode until licensing can be refreshed.");
            }

            if (!settingsLoad.UsedFallback)
            {
                try
                {
                    if (await ReconcileSettingsReferencesAtStartupAsync())
                        warnings.Add("Removed stale workflow, Magic Action, or capture-profile references from local settings before hotkeys were initialized.");
                }
                catch (Exception ex)
                {
                    Services.Log.Error("SettingsReferenceStartupReconcile", ex);
                    warnings.Add("Some stale settings references could not be reconciled. Review capture profiles and personal hotkeys before relying on them.");
                }
            }

            try
            {
                if (!TryApplyHotkeysForSettings(Services.Settings))
                    warnings.Add("One or more configured global hotkeys could not be registered. Existing tray and main-window commands remain available.");
            }
            catch (Exception ex)
            {
                Services.Log.Error("HotkeyInitialize", ex);
                warnings.Add("Global hotkeys could not be initialized. Capture remains available from the tray and main window.");
            }

            try
            {
                await Services.WorkflowTriggerEngine.ReloadAsync();
            }
            catch (Exception ex)
            {
                Services.Log.Error("WorkflowTriggerInitialize", ex);
                warnings.Add("Workflow automation triggers could not be initialized. Workflows remain available manually.");
            }

            try
            {
                await Services.HistoryStore.ApplyRetentionAsync(Services.Settings);
            }
            catch (Exception ex)
            {
                Services.Log.Error("HistoryRetentionInitialize", ex);
                warnings.Add("History retention initialization failed. Existing History data was left untouched.");
            }

            var startup = await Services.Startup.GetStateAsync();
            Services.Tray.SetTier(Services.Entitlements.Current.Tier);
            _mainWindow?.ApplySettingsToUi(Services.Settings, CombinedHotkeyError());
            _mainWindow?.ApplyStartupState(startup);
            _mainWindow?.ApplyEntitlementToUi(Services.Entitlements.Current);
            if (warnings.Count > 0)
                _mainWindow?.ShowStatus(string.Join(" ", warnings.Distinct()), InfoBarSeverity.Warning);

            if (_startupActivation)
            {
                HideMainWindow();
            }
            else if (_launchCliArgs.Length == 0 && Services.Entitlements.ShouldShowTrialExpiredNotice && _mainWindow is not null)
            {
                await _mainWindow.ShowTrialExpiredAsync();
            }

            if (!_startupActivation && _launchCliArgs.Length > 0)
            {
                var pending = _launchCliArgs;
                _launchCliArgs = [];
                await HandleCliArgsAsync(pending);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error("Startup", ex);
            _mainWindow?.ShowStatus("Startup encountered an unexpected error. The resident app remains available; see the local log for details.", InfoBarSeverity.Error);
        }
    }

    private async Task HandleCliArgsAsync(IReadOnlyList<string> args)
    {
        var parsed = CliParser.Parse(args);
        if (!parsed.IsValid)
        {
            ShowMainWindow();
            _mainWindow?.ShowStatus(parsed.Error ?? "Invalid command line.", InfoBarSeverity.Error);
            return;
        }
        if (parsed.Command is null) { ShowMainWindow(); return; }

        try
        {
            switch (parsed.Command)
            {
                case CaptureCliCommand capture:
                    switch (capture.Kind)
                    {
                        case CaptureCommandKind.Region: await CaptureRegionFromUiAsync(); break;
                        case CaptureCommandKind.Monitor: await CaptureActiveMonitorAsync(); break;
                        case CaptureCommandKind.Desktop: await CaptureVirtualDesktopAsync(); break;
                    }
                    break;

                case WorkflowCliCommand workflowCommand:
                    await CaptureRegionForWorkflowAsync(workflowCommand.Name, workflowCommand.Variables);
                    break;

                case TriggerCliCommand triggerCommand:
                    await Services.WorkflowTriggerRunner.RunAsync(
                        triggerCommand.Id,
                        WorkflowTriggerKind.Schedule,
                        "schedule");
                    break;

                case OpenCliCommand open:
                    ShowMainWindow();
                    switch (open.Page)
                    {
                        case OpenPage.History: _mainWindow?.ShowHistory(); break;
                        case OpenPage.Settings: _mainWindow?.ShowSettings(); break;
                        case OpenPage.Plan: _mainWindow?.ShowPlan(); break;
                        case OpenPage.Ai: _mainWindow?.ShowAiSettings(); break;
                        case OpenPage.Workflows: _mainWindow?.ShowWorkflows(); break;
                        case OpenPage.Utilities: _mainWindow?.ShowUtilities(); break;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error("CLI", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task CaptureRegionForWorkflowAsync(string workflowNameOrId, IReadOnlyDictionary<string, string>? variables = null)
    {
        var workflows = await Services.Workflows.LoadAsync();
        var workflow = workflows.FirstOrDefault(w =>
            string.Equals(w.Id, workflowNameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(w.Name, workflowNameOrId, StringComparison.OrdinalIgnoreCase));
        if (workflow is null)
            throw new InvalidOperationException($"Workflow not found: {workflowNameOrId}");
        if (Services.Entitlements.Current.Tier < workflow.RequiredTier)
        {
            ShowMainWindow();
            _mainWindow?.ShowStatus($"{workflow.Name} requires {workflow.RequiredTier}.", InfoBarSeverity.Warning);
            return;
        }

        var result = await Services.Capture.CaptureRegionAsync(
            OverlayCaptureAction.Workflow,
            Services.Settings.CaptureCursor,
            Services.Entitlements.Current.Tier,
            workflow.Id,
            Services.Settings.CaptureOverlayTheme,
            actionLayout: Services.Settings.OverlayActions);
        if (result is null) return;
        foreach (var asset in result.Assets)
            await RunWorkflowAsync(asset, workflow.Id, variables);
    }

    private void Entitlements_Changed(object? sender, EntitlementSnapshot snapshot)
    {
        ConfigureProHotkey();
        Services.Tray.SetTier(snapshot.Tier);
        _mainWindow?.ApplyEntitlementToUi(snapshot);
    }

    private void ConfigureProHotkey()
    {
        if (!TryApplyHotkeysForSettings(Services.Settings))
        {
            Services.Log.Error("EntitlementHotkeyReapply", CreateHotkeyConfigurationException("Global hotkeys could not be reconciled after entitlement changed."));
            _mainWindow?.ApplySettingsToUi(Services.Settings, CombinedHotkeyError());
        }
    }

    private void WireResidentEvents()
    {
        Services.Hotkeys.RegionCaptureRequested += async (_, _) => await CaptureRegionFromUiAsync();
        Services.Hotkeys.RepeatRegionRequested += async (_, _) => await CaptureRepeatRegionAsync();
        Services.Hotkeys.PersonalHotkeyRequested += async (_, e) => await DispatchPersonalHotkeyAsync(e.Binding);
        Services.Tray.RegionCaptureRequested += async (_, _) => await CaptureRegionFromUiAsync();
        Services.Tray.RepeatRegionRequested += async (_, _) => await CaptureRepeatRegionAsync();
        Services.Tray.MonitorCaptureRequested += async (_, _) => await CaptureActiveMonitorAsync();
        Services.Tray.VirtualDesktopCaptureRequested += async (_, _) => await CaptureVirtualDesktopAsync();
        Services.Tray.WindowCaptureRequested += async (_, _) => await CaptureForegroundWindowAsync();
        Services.Tray.OpenRequested += (_, _) => ShowMainWindow();
        Services.Tray.HistoryRequested += (_, _) => { ShowMainWindow(); _mainWindow?.ShowHistory(); };
        Services.Tray.SettingsRequested += (_, _) => { ShowMainWindow(); _mainWindow?.ShowSettings(); };
        Services.Tray.PlanRequested += (_, _) => { ShowMainWindow(); _mainWindow?.ShowPlan(); };
        Services.Tray.RestorePinsRequested += (_, _) => RestorePinInteraction();
        Services.Tray.ExitRequested += (_, _) => ExitApplication();
    }

    internal async Task CaptureRegionFromUiAsync(int delaySeconds = 0)
    {
        try
        {
            HideMainWindow();
            await Task.Delay(TimeSpan.FromMilliseconds(delaySeconds > 0 ? delaySeconds * 1000 : 180));
            if (await TryRunForegroundAppCaptureRuleAsync()) return;
            var result = await Services.Capture.CaptureRegionAsync(
                DefaultOverlayAction(),
                Services.Settings.CaptureCursor,
                Services.Entitlements.Current.Tier,
                overlayTheme: Services.Settings.CaptureOverlayTheme,
                actionLayout: Services.Settings.OverlayActions);
            if (result is not null)
            {
                await RememberRegionAsync(result.SelectionBounds);
                await HandleCaptureRequestAsync(result);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error("RegionCapture", ex);
        }
    }

    internal async Task RunCaptureProfileAsync(CaptureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            await RunCaptureProfileCoreAsync(profile.Normalize(), automation: false, workflow: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Services.Log.Error("CaptureProfile", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message);
        }
    }

    internal Task RunCaptureProfileForAutomationAsync(
        CaptureProfile profile,
        CaptureWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(workflow);
        profile = profile.Normalize();
        if (!WorkflowTriggerPolicy.IsCaptureProfileUnattendedSafe(profile))
            throw new InvalidOperationException("Capture profile requires interactive input and cannot run from automation.");
        return RunCaptureProfileCoreAsync(profile with { WorkflowId = workflow.Id }, automation: true, workflow, cancellationToken);
    }

    private async Task RunCaptureProfileCoreAsync(
        CaptureProfile profile,
        bool automation,
        CaptureWorkflow? workflow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var action = profile.PostCaptureAction switch
        {
            PostCaptureAction.CopyImage => OverlayCaptureAction.Copy,
            PostCaptureAction.PinImage => OverlayCaptureAction.Pin,
            PostCaptureAction.Save => OverlayCaptureAction.Save,
            _ => OverlayCaptureAction.Result
        };

        CaptureAsset? asset = null;
        if (profile.Source == CaptureProfileSource.ForegroundWindow)
        {
            if (!automation) HideMainWindow();
            await Task.Delay(Math.Max(250, profile.DelayMilliseconds), cancellationToken);
            asset = Services.WindowCapture.CaptureForegroundWindow(profile.CaptureCursor);
        }
        else
        {
            if (!automation) HideMainWindow();
            await Task.Delay(Math.Max(180, profile.DelayMilliseconds), cancellationToken);
            switch (profile.Source)
            {
                case CaptureProfileSource.Region when profile.Region is { } region:
                    asset = Services.Capture.CaptureExactRegion(region, profile.CaptureCursor, profile.Name);
                    if (!automation) await RememberRegionAsync(asset.PixelBounds);
                    break;
                case CaptureProfileSource.Region:
                    if (automation)
                        throw new InvalidOperationException("Interactive region capture cannot run through automation.");
                    var result = await Services.Capture.CaptureRegionAsync(
                        action,
                        profile.CaptureCursor,
                        Services.Entitlements.Current.Tier,
                        profile.WorkflowId,
                        Services.Settings.CaptureOverlayTheme,
            actionLayout: Services.Settings.OverlayActions);
                    if (result is null) return;
                    await RememberRegionAsync(result.SelectionBounds);
                    if (!string.IsNullOrWhiteSpace(profile.WorkflowId))
                    {
                        foreach (var resultAsset in result.Assets)
                            await RunWorkflowAsync(resultAsset, profile.WorkflowId);
                    }
                    else
                    {
                        await HandleCaptureRequestAsync(result, profile.FileFormat);
                    }
                    return;
                case CaptureProfileSource.ActiveMonitor:
                    asset = Services.Capture.CaptureActiveMonitor(profile.CaptureCursor);
                    break;
                case CaptureProfileSource.VirtualDesktop:
                    asset = Services.Capture.CaptureVirtualDesktop(profile.CaptureCursor);
                    break;
                case CaptureProfileSource.Scrolling:
                    throw new InvalidOperationException("Scrolling capture requires interactive coordination and cannot run through automation.");
            }
        }

        if (asset is null) throw new InvalidOperationException("Capture profile did not produce an asset.");
        if (workflow is not null)
        {
            await RunWorkflowForAutomationAsync(asset, workflow, cancellationToken);
            return;
        }
        if (!string.IsNullOrWhiteSpace(profile.WorkflowId))
            await RunWorkflowAsync(asset, profile.WorkflowId);
        else
            await HandleCaptureAsync(asset, action, saveFormat: profile.FileFormat);
    }

    private async Task RunWorkflowForAutomationAsync(CaptureAsset asset, CaptureWorkflow workflow, CancellationToken cancellationToken)
    {
        var workflowAsset = await PrepareWorkflowAssetAsync(asset, workflow, cancellationToken);
        WorkflowExecutionResult result;
        try
        {
            result = await Services.WorkflowExecutor.ExecuteAsync(workflow, CreateWorkflowExecutionContext(workflowAsset), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StoreWorkflowFailureTraceBestEffortAsync(workflow, dryRun: false, assetId: asset.Id, cancellationToken: cancellationToken);
            throw;
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            await StoreWorkflowFailureTraceBestEffortAsync(workflow, dryRun: false, assetId: asset.Id, cancellationToken: cancellationToken);
            throw;
        }
        await StoreWorkflowTraceBestEffortAsync(workflow, result, assetId: asset.Id, cancellationToken: cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException("Workflow execution failed.");
    }

    private async Task RememberRegionAsync(PixelRect region)
    {
        if (region.IsEmpty) return;
        await TryMutateSettingsAsync(
            current => current with { RecentRegions = RecentCaptureRegions.Push(current.RecentRegions, region) },
            SettingsRuntimeEffects.MainWindowUi,
            logComponent: "RecentRegionSave");
    }

    internal async Task CaptureRepeatRegionAsync()
    {
        if (!EnsureFeature(ProductFeature.RepeatLastRegion)) return;
        try
        {
            var asset = Services.Capture.CaptureLastRegion(Services.Settings.CaptureCursor);
            await HandleCaptureAsync(asset, DefaultOverlayAction());
        }
        catch (Exception ex)
        {
            Services.Log.Error("RepeatRegion", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message);
        }
    }

    internal async Task CaptureAutomaticScrollAsync()
    {
        if (!EnsureFeature(ProductFeature.ScrollingStitch)) return;
        try
        {
            ShowMainWindow();
            await Task.Delay(60);
            var xamlRoot = _mainWindow?.Content?.XamlRoot ?? throw new InvalidOperationException("The main window is not ready for scrolling capture.");
            var mode = await ScrollingCaptureModeDialog.ShowAsync(xamlRoot);
            if (mode is null) return;

            HideMainWindow();
            await Task.Delay(180);
            var selection = await Services.Capture.CaptureRegionAsync(
                OverlayCaptureAction.Result,
                includeCursor: false,
                Services.Entitlements.Current.Tier,
                overlayTheme: Services.Settings.CaptureOverlayTheme,
                rectangularOnly: true,
                actionLayout: Services.Settings.OverlayActions);
            if (selection is null) { ShowMainWindow(); return; }
            await RememberRegionAsync(selection.SelectionBounds);

            if (mode.Mode == ScrollingCaptureMode.Grid2D)
            {
                _mainWindow?.ShowStatus($"2D scrolling capture started · {mode.Rows}×{mode.Columns} grid…");
                var gridProgress = new Progress<TwoDimensionalScrollCaptureProgress>(state =>
                {
                    var phase = state.Phase == "Stitching grid"
                        ? $"Stitching {state.TotalTiles} 2D tile(s)…"
                        : $"2D capture · tile {Math.Min(state.CapturedTiles + 1, state.TotalTiles)}/{state.TotalTiles} · row {state.Row + 1}, column {state.Column + 1}";
                    _mainWindow?.ShowStatus(phase);
                });
                var grid = await Services.TwoDimensionalScroll.CaptureAsync(
                    selection.Asset.PixelBounds,
                    new TwoDimensionalScrollCaptureOptions(Rows: mode.Rows, Columns: mode.Columns),
                    gridProgress);
                var gridAsset = selection.Asset.WithPng(grid.PngBytes) with
                {
                    Id = Guid.NewGuid(),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    SourceDisplayName = $"2D scrolling · {grid.Rows}×{grid.Columns} · {grid.TileCount} tiles"
                };
                await HandleCaptureAsync(gridAsset, OverlayCaptureAction.Result);
                ShowMainWindow();
                _mainWindow?.ShowStatus($"2D scrolling capture completed · {grid.Rows}×{grid.Columns} · {grid.TileCount} tiles.", InfoBarSeverity.Success);
                return;
            }

            var axis = mode.Mode == ScrollingCaptureMode.Horizontal ? ScrollAxis.Horizontal : ScrollAxis.Vertical;
            _mainWindow?.ShowStatus(axis == ScrollAxis.Horizontal ? "Horizontal scrolling capture started…" : "Automatic scrolling capture started…");
            var progress = new Progress<AutomaticScrollCaptureProgress>(state =>
            {
                var message = state.Phase switch
                {
                    "Stitching" => $"Stitching {state.FrameCount} frame(s)…",
                    "Stitching horizontal" => $"Stitching {state.FrameCount} horizontal frame(s)…",
                    "Waiting for dynamic content" => $"Scrolling capture: waiting for animation to settle · {state.LastChangedPixelPercent:0.#}% changing",
                    var phase when phase.StartsWith("Alignment retry", StringComparison.Ordinal) => $"Scrolling capture: {phase.ToLowerInvariant()}…",
                    var phase when phase.StartsWith("Horizontal alignment retry", StringComparison.Ordinal) => $"Scrolling capture: {phase.ToLowerInvariant()}…",
                    "Alignment corrected" => "Scrolling capture: alignment corrected; continuing…",
                    "Horizontal alignment corrected" => "Horizontal scrolling capture: alignment corrected; continuing…",
                    "Capturing · sticky chrome removed" => $"Scrolling capture: {state.FrameCount} frame(s) · removing repeated header/footer",
                    "Checking end" => $"Scrolling capture: checking page end · {state.LastChangedPixelPercent:0.#}% changed",
                    "Checking horizontal end" => $"Horizontal scrolling: checking content end · {state.LastChangedPixelPercent:0.#}% changed",
                    "Capturing horizontal" => $"Horizontal scrolling capture: {state.FrameCount} frame(s)",
                    _ => $"Scrolling capture: {state.FrameCount} frame(s)"
                };
                _mainWindow?.ShowStatus(message);
            });
            var capture = await Services.AutomaticScroll.CaptureAsync(
                selection.Asset.PixelBounds,
                new AutomaticScrollCaptureOptions(Axis: axis),
                progress);
            var asset = selection.Asset.WithPng(capture.PngBytes) with
            {
                Id = Guid.NewGuid(),
                CreatedUtc = DateTimeOffset.UtcNow,
                SourceDisplayName = axis == ScrollAxis.Horizontal
                    ? $"Horizontal scrolling · {capture.FrameCount} frames"
                    : $"Automatic scrolling · {capture.FrameCount} frames"
            };
            await HandleCaptureAsync(asset, OverlayCaptureAction.Result);
            ShowMainWindow();
            var captureNotes = new List<string>();
            if (capture.StickyTopRowsRemoved + capture.StickyBottomRowsRemoved > 0)
                captureNotes.Add("repeated header/footer removed");
            if (capture.AlignmentRetries > 0)
                captureNotes.Add($"{capture.AlignmentRetries} alignment correction(s)");
            if (capture.DynamicContentDetected)
                captureNotes.Add("dynamic content detected");
            var noteSuffix = captureNotes.Count == 0 ? string.Empty : $" · {string.Join(" · ", captureNotes)}";
            var axisLabel = axis == ScrollAxis.Horizontal ? "Horizontal scrolling capture" : "Scrolling capture";
            _mainWindow?.ShowStatus(capture.EndDetected
                ? $"{axisLabel} completed with {capture.FrameCount} frame(s){noteSuffix}."
                : $"{axisLabel} reached the safety frame limit ({capture.FrameCount}){noteSuffix}. Review the stitched result.",
                capture.EndDetected ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("AutomaticScroll", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus($"Scrolling capture: {ex.Message}", InfoBarSeverity.Warning);
        }
    }

    private MonitorCapturePreference? ResolveMonitorCapturePreferences(MonitorInfo monitor) =>
        Services.Settings.MonitorPreferences.FirstOrDefault(preference =>
            string.Equals(preference.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));

    private AppCaptureRule? ResolveAppCaptureRule(WindowCaptureTarget? target)
    {
        if (target is null) return null;
        var executable = !string.IsNullOrWhiteSpace(target.ExecutablePath)
            ? Path.GetFileName(target.ExecutablePath)
            : string.IsNullOrWhiteSpace(target.ProcessName) ? null : target.ProcessName + ".exe";
        if (string.IsNullOrWhiteSpace(executable)) return null;
        return Services.Settings.AppCaptureRules.FirstOrDefault(rule => rule.Enabled &&
            string.Equals(rule.ExecutableName, executable, StringComparison.OrdinalIgnoreCase));
    }

    internal async Task CaptureActiveMonitorAsync(int delaySeconds = 0)
    {
        try
        {
            var monitor = Services.Monitors.GetActiveMonitor();
            var preference = ResolveMonitorCapturePreferences(monitor);
            var captureCursor = preference?.CaptureCursor ?? Services.Settings.CaptureCursor;
            var action = preference?.PostCaptureAction is { } overrideAction ? OverlayActionFor(overrideAction) : DefaultOverlayAction();
            HideMainWindow();
            await Task.Delay(TimeSpan.FromMilliseconds(delaySeconds > 0 ? delaySeconds * 1000 : 180));
            await HandleCaptureAsync(Services.Capture.CaptureMonitor(monitor, captureCursor), action);
        }
        catch (Exception ex)
        {
            Services.Log.Error("MonitorCapture", ex);
            ShowMainWindow();
        }
    }

    internal async Task CaptureMonitorTargetAsync(MonitorInfo monitor, int delaySeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        try
        {
            var preference = ResolveMonitorCapturePreferences(monitor);
            var captureCursor = preference?.CaptureCursor ?? Services.Settings.CaptureCursor;
            var action = preference?.PostCaptureAction is { } overrideAction ? OverlayActionFor(overrideAction) : DefaultOverlayAction();
            HideMainWindow();
            await Task.Delay(TimeSpan.FromMilliseconds(delaySeconds > 0 ? delaySeconds * 1000 : 180));
            await HandleCaptureAsync(Services.Capture.CaptureMonitor(monitor, captureCursor), action);
        }
        catch (Exception ex)
        {
            Services.Log.Error("MonitorTargetCapture", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    internal Task CaptureWindowTargetAsync(WindowCaptureTarget target, int delaySeconds = 0) =>
        CaptureWindowTargetsAsync([target], delaySeconds);

    internal async Task CaptureWindowTargetsAsync(IReadOnlyList<WindowCaptureTarget> targets, int delaySeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var selected = targets.Where(target => target is not null).DistinctBy(target => target.Handle).Take(16).ToArray();
        if (selected.Length == 0) return;
        try
        {
            HideMainWindow();
            await Task.Delay(TimeSpan.FromMilliseconds(delaySeconds > 0 ? delaySeconds * 1000 : 250));
            var captured = 0;
            var failed = 0;
            foreach (var target in selected)
            {
                try
                {
                    var asset = Services.WindowCapture.CaptureWindow(target, Services.Settings.CaptureCursor);
                    await HandleCaptureAsync(asset, DefaultOverlayAction());
                    captured++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    failed++;
                    Services.Log.Error("WindowTargetItemCapture", ex);
                }
            }
            if (failed > 0)
            {
                ShowMainWindow();
                _mainWindow?.ShowStatus($"Captured {captured} window(s) · {failed} could not be captured.", InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error("WindowTargetCapture", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    internal async Task CaptureVirtualDesktopAsync(int delaySeconds = 0)
    {
        try
        {
            HideMainWindow();
            await Task.Delay(TimeSpan.FromMilliseconds(delaySeconds > 0 ? delaySeconds * 1000 : 180));
            await HandleCaptureAsync(Services.Capture.CaptureVirtualDesktop(Services.Settings.CaptureCursor), DefaultOverlayAction());
        }
        catch (Exception ex)
        {
            Services.Log.Error("VirtualDesktopCapture", ex);
            ShowMainWindow();
        }
    }


    private async Task<bool> TryRunForegroundAppCaptureRuleAsync()
    {
        var target = Services.WindowCapture.TryGetForegroundTarget();
        var rule = ResolveAppCaptureRule(target);
        if (rule is null) return false;
        var profile = Services.Settings.CaptureProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, rule.CaptureProfileId, StringComparison.Ordinal));
        if (profile is null) return false;
        profile = profile with
        {
            CaptureCursor = rule.CaptureCursor ?? profile.CaptureCursor,
            PostCaptureAction = rule.PostCaptureAction ?? profile.PostCaptureAction
        };
        await RunCaptureProfileAsync(profile);
        return true;
    }

    internal async Task CaptureForegroundWindowAsync(int delaySeconds = 0)
    {
        try
        {
            HideMainWindow();
            await Task.Delay(TimeSpan.FromMilliseconds(delaySeconds > 0 ? delaySeconds * 1000 : 250));
            if (await TryRunForegroundAppCaptureRuleAsync()) return;
            await HandleCaptureAsync(Services.WindowCapture.CaptureForegroundWindow(Services.Settings.CaptureCursor), DefaultOverlayAction());
        }
        catch (Exception ex)
        {
            Services.Log.Error("WindowCapture", ex);
            ShowMainWindow();
        }
    }

    private static OverlayCaptureAction OverlayActionFor(PostCaptureAction action) => action switch
    {
        PostCaptureAction.CopyImage => OverlayCaptureAction.Copy,
        PostCaptureAction.PinImage => OverlayCaptureAction.Pin,
        PostCaptureAction.Save => OverlayCaptureAction.Save,
        _ => OverlayCaptureAction.Result
    };

    private OverlayCaptureAction DefaultOverlayAction() => Services.Settings.DefaultPostCaptureAction switch
    {
        PostCaptureAction.CopyImage => OverlayCaptureAction.Copy,
        PostCaptureAction.PinImage => OverlayCaptureAction.Pin,
        PostCaptureAction.Save => OverlayCaptureAction.Save,
        _ => OverlayCaptureAction.Result
    };

    private async Task HandleCaptureRequestAsync(CaptureRequestResult result, string? saveFormat = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Assets.Count == 0) return;
        if (result.Assets.Count == 1)
        {
            await HandleCaptureAsync(result.Asset, result.Action, result.WorkflowId, saveFormat);
            return;
        }

        if (result.Action == OverlayCaptureAction.Save)
        {
            await SaveSeparateCaptureAssetsAsync(result.Assets, saveFormat);
            return;
        }
        if (result.Action == OverlayCaptureAction.Result)
        {
            foreach (var asset in result.Assets)
                await AddCaptureToHistoryBestEffortAsync(asset, "HistoryAdd.MultiRegion");
            ShowMainWindow();
            _mainWindow?.ShowHistory();
            _mainWindow?.ShowStatus($"Captured {result.Assets.Count} separate regions into History.", InfoBarSeverity.Success);
            _mainWindow?.RefreshHistorySoon();
            return;
        }
        if (result.Action != OverlayCaptureAction.Workflow)
            throw new InvalidOperationException("Separate multi-region output supports Open, Save, or Workflow. Use Canvas output for one-image actions.");

        foreach (var asset in result.Assets)
            await HandleCaptureAsync(asset, result.Action, result.WorkflowId, saveFormat);
    }

    private async Task AddCaptureToHistoryBestEffortAsync(CaptureAsset asset, string logScope)
    {
        try
        {
            await Services.HistoryStore.AddAsync(asset, Services.Settings);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            Services.Log.Error(logScope, ex);
        }
    }

    private async Task SaveSeparateCaptureAssetsAsync(IReadOnlyList<CaptureAsset> assets, string? saveFormat)
    {
        if (assets.Count == 0) return;
        foreach (var asset in assets)
            await AddCaptureToHistoryBestEffortAsync(asset, "HistoryAdd.MultiRegion");

        if (Services.Settings.AutoCopyImage)
            _mainWindow?.ShowStatus("Auto-copy is skipped for separate multi-region output because the clipboard holds one image. Use Canvas output to auto-copy.", InfoBarSeverity.Informational);

        if (_mainWindow is null) return;
        var folder = await Services.Export.PickImageOutputFolderAsync(_mainWindow);
        if (folder is null)
        {
            _mainWindow.RefreshHistorySoon();
            return;
        }

        var format = string.IsNullOrWhiteSpace(saveFormat) ? "png" : saveFormat;
        var saved = 0;
        var failed = 0;
        for (var i = 0; i < assets.Count; i++)
        {
            try
            {
                var saveAsset = await ApplyOutboundRedactionAsync(assets[i], Services.Settings.RedactBeforeSave, "save");
                await Services.Export.SaveImageToFolderAsync(
                    folder,
                    saveAsset,
                    format,
                    Services.Settings.JpegQuality,
                    Services.Settings.FileNameTemplate,
                    i + 1);
                saved++;
            }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
            {
                failed++;
                Services.Log.Error("MultiRegionSave", ex);
            }
        }

        _mainWindow.ShowStatus(
            $"Saved {saved} separate region image(s)" + (failed > 0 ? $" · {failed} failed" : string.Empty),
            failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        _mainWindow.RefreshHistorySoon();
    }

    internal async Task HandleCaptureAsync(CaptureAsset asset, OverlayCaptureAction action, string? workflowId = null, string? saveFormat = null)
    {
        if (Services.Settings.AutoCopyImage && action != OverlayCaptureAction.Copy)
        {
            var copyAsset = await ApplyOutboundRedactionAsync(asset, Services.Settings.RedactBeforeCopy, "copy");
            await Services.Clipboard.CopyImageAsync(copyAsset.PngBytes);
        }

        try
        {
            // History intentionally preserves the original local capture. Outbound redaction policies
            // affect only the operation leaving History/editor state: copy, save, pin, or workflow.
            await Services.HistoryStore.AddAsync(asset, Services.Settings);
        }
        catch (Exception ex)
        {
            Services.Log.Error("HistoryAdd", ex);
        }

        switch (action)
        {
            case OverlayCaptureAction.Copy:
            {
                var copyAsset = await ApplyOutboundRedactionAsync(asset, Services.Settings.RedactBeforeCopy, "copy");
                await Services.Clipboard.CopyImageAsync(copyAsset.PngBytes);
                break;
            }
            case OverlayCaptureAction.Pin:
            {
                var pinAsset = await ApplyOutboundRedactionAsync(asset, Services.Settings.RedactBeforePin, "pin");
                OpenPin(pinAsset);
                break;
            }
            case OverlayCaptureAction.Save:
            {
                var saveAsset = await ApplyOutboundRedactionAsync(asset, Services.Settings.RedactBeforeSave, "save");
                if (_mainWindow is not null)
                    await Services.Export.SaveImageAsAsync(_mainWindow, saveAsset, string.IsNullOrWhiteSpace(saveFormat) ? "png" : saveFormat, Services.Settings.JpegQuality, Services.Settings.FileNameTemplate);
                break;
            }
            case OverlayCaptureAction.Text:
                await CopyRecognizedTextAsync(asset);
                break;
            case OverlayCaptureAction.Table:
                if (EnsureFeature(ProductFeature.TableExtraction)) OpenResult(asset, CaptureResultTab.Table);
                break;
            case OverlayCaptureAction.Barcode:
                if (EnsureFeature(ProductFeature.BarcodeRecognition)) OpenResult(asset, CaptureResultTab.Barcode);
                break;
            case OverlayCaptureAction.Edit:
                OpenAnnotation(asset);
                break;
            case OverlayCaptureAction.Color:
                CopyCenterColor(asset);
                break;
            case OverlayCaptureAction.Magic:
                if (EnsureFeature(ProductFeature.MagicActions)) OpenMagic(asset);
                break;
            case OverlayCaptureAction.Workflow:
                if (!string.IsNullOrWhiteSpace(workflowId)) await RunWorkflowAsync(asset, workflowId);
                break;
            default:
                OpenResult(asset);
                break;
        }
        _mainWindow?.RefreshHistorySoon();
    }

    private async Task<CaptureAsset> ApplyOutboundRedactionAsync(CaptureAsset asset, bool enabled, string operation, CancellationToken cancellationToken = default)
    {
        if (!enabled) return asset;
        try
        {
            var result = await Services.Redaction.RedactAsync(asset, Services.Settings, cancellationToken);
            if (result.LayerCount > 0)
                _mainWindow?.ShowStatus($"Local privacy: redacted {result.LayerCount} region(s) before {operation}.", InfoBarSeverity.Informational);
            return result.Asset;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Services.Log.Error($"RedactBefore{operation}", ex);
            throw new InvalidOperationException($"{operation} was blocked because local redaction is enabled but could not complete: {ex.Message}", ex);
        }
    }

    internal async Task<bool> ConfirmWorkflowCloudAiAsync(MagicActionExecutionRequest request, MagicActionExecutionPreview preview, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = await Services.ScreenGraph.BuildAsync(request.Primary, Services.Settings, cancellationToken);
        var guardText = string.Join("\n", graph.Nodes.Select(node => node.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        var guardFindings = AiGuard.Scan(guardText);
        var critical = guardFindings.Count(finding => finding.Severity == AiGuardSeverity.Critical);
        var warning = guardFindings.Count(finding => finding.Severity == AiGuardSeverity.Warning);

        ShowMainWindow();
        if (_mainWindow?.Content?.XamlRoot is null) return false;
        var details = new List<string>
        {
            $"Provider: {preview.ProviderName}",
            $"Model: {preview.ModelId}",
            $"Images: {preview.Payload.ImageCount}",
            $"Context items: {preview.Payload.ContextItemCount}",
            "Magic Capture Desktop sends directly to your configured provider; it does not proxy the request."
        };
        if (critical > 0 || warning > 0)
            details.Add($"AI Guard detected {critical} critical and {warning} warning finding(s) in captured text. Review the capture before sending secrets or personal data.");

        var dialog = new ContentDialog
        {
            XamlRoot = _mainWindow.Content.XamlRoot,
            Title = "Send workflow step to cloud AI?",
            Content = string.Join("\n\n", details),
            PrimaryButtonText = critical > 0 ? "Send anyway" : "Run",
            CloseButtonText = "Cancel",
            DefaultButton = critical > 0 ? ContentDialogButton.Close : ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    internal async Task<bool> ConfirmLocalActionApprovalAsync(LocalActionApprovalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShowMainWindow();
        if (_mainWindow?.Content?.XamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            XamlRoot = _mainWindow.Content.XamlRoot,
            Title = "Allow this Local Action program?",
            Content = string.Join("\n\n",
                $"Action: {request.Profile.Name}",
                $"Executable: {request.ExecutablePath}",
                $"SHA-256: {request.Sha256}",
                "Magic Capture Desktop will launch this exact executable directly without a shell. The approval is pinned to this file hash, so a changed binary must be approved again.",
                "The program runs with your desktop account permissions and may receive capture data through the Local Action variables you configured."),
            PrimaryButtonText = "Allow this binary",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    internal WorkflowExecutionContext CreateWorkflowExecutionContext(
        CaptureAsset asset,
        IReadOnlyDictionary<string, string>? initialVariables = null,
        bool dryRun = false,
        IReadOnlyList<CaptureAsset>? loopAssets = null) =>
        new(
            asset,
            SaveImageAsync: async (image, _) =>
            {
                if (_mainWindow is not null)
                    await Services.Export.SaveImageAsAsync(_mainWindow, image, "png", Services.Settings.JpegQuality, Services.Settings.FileNameTemplate);
            },
            PinImage: OpenPin,
            OpenEditor: OpenAnnotation,
            AiContext: Services.AiContext.Assets,
            ConfirmCloudMagicActionAsync: ConfirmWorkflowCloudAiAsync,
            InitialVariables: initialVariables,
            ConfirmLocalActionApprovalAsync: ConfirmLocalActionApprovalAsync,
            PromptTextAsync: PromptWorkflowTextAsync,
            PromptChoiceAsync: PromptWorkflowChoiceAsync,
            ConfirmStepAsync: ConfirmWorkflowStepAsync,
            ResolveWorkflowAsync: ResolveWorkflowByIdAsync,
            DryRun: dryRun,
            LoopAssets: loopAssets ?? new[] { asset });

    private async Task<string?> PromptWorkflowTextAsync(string prompt, string? defaultValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShowMainWindow();
        if (_mainWindow?.Content?.XamlRoot is null) return null;
        var box = new TextBox
        {
            Header = prompt,
            Text = defaultValue ?? string.Empty,
            MaxLength = WorkflowRuntimePolicy.MaximumParameterValueLength,
            MinWidth = 380
        };
        var dialog = new ContentDialog
        {
            XamlRoot = _mainWindow.Content.XamlRoot,
            Title = "Workflow input",
            Content = box,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary ? box.Text : null;
    }

    private async Task<string?> PromptWorkflowChoiceAsync(
        string prompt,
        IReadOnlyList<string> choices,
        string? defaultValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShowMainWindow();
        if (_mainWindow?.Content?.XamlRoot is null || choices.Count == 0) return null;
        var combo = new ComboBox
        {
            Header = prompt,
            ItemsSource = choices,
            MinWidth = 380
        };
        var defaultIndex = defaultValue is null ? -1 : choices.ToList().FindIndex(choice => string.Equals(choice, defaultValue, StringComparison.Ordinal));
        combo.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
        var dialog = new ContentDialog
        {
            XamlRoot = _mainWindow.Content.XamlRoot,
            Title = "Workflow choice",
            Content = combo,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary ? combo.SelectedItem as string : null;
    }

    private async Task<bool?> ConfirmWorkflowStepAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShowMainWindow();
        if (_mainWindow?.Content?.XamlRoot is null) return null;
        var dialog = new ContentDialog
        {
            XamlRoot = _mainWindow.Content.XamlRoot,
            Title = "Workflow confirmation",
            Content = prompt,
            PrimaryButtonText = "Yes",
            SecondaryButtonText = "No",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null
        };
    }

    private async Task<CaptureWorkflow?> ResolveWorkflowByIdAsync(string workflowId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await Services.Workflows.LoadAsync(cancellationToken))
            .FirstOrDefault(workflow => string.Equals(workflow.Id, workflowId, StringComparison.Ordinal));
    }

    internal async Task StoreWorkflowTraceBestEffortAsync(
        CaptureWorkflow workflow,
        WorkflowExecutionResult result,
        Guid? assetId = null,
        Guid? resumedFromTraceId = null,
        IReadOnlyCollection<string>? resumeCompletedSideEffectStepIds = null,
        CancellationToken cancellationToken = default)
    {
        try { await Services.WorkflowTraces.AppendAsync(workflow, result, assetId, resumedFromTraceId, resumeCompletedSideEffectStepIds, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { Services.Log.Error("WorkflowTrace", ex); }
    }

    internal async Task StoreWorkflowFailureTraceBestEffortAsync(
        CaptureWorkflow workflow,
        bool dryRun,
        Guid? assetId = null,
        Guid? resumedFromTraceId = null,
        IReadOnlyCollection<string>? resumeCompletedSideEffectStepIds = null,
        CancellationToken cancellationToken = default)
    {
        try { await Services.WorkflowTraces.AppendFailureAsync(workflow, dryRun, assetId, resumedFromTraceId, resumeCompletedSideEffectStepIds, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { Services.Log.Error("WorkflowTraceFailure", ex); }
    }

    internal async Task<CaptureAsset> PrepareWorkflowAssetAsync(
        CaptureAsset asset,
        CaptureWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        var redactWorkflow = await ShouldRedactWorkflowAsync(workflow, cancellationToken);
        return await PrepareWorkflowAssetAsync(asset, redactWorkflow, cancellationToken);
    }

    internal async Task<bool> ShouldRedactWorkflowAsync(
        CaptureWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        cancellationToken.ThrowIfCancellationRequested();
        var settings = Services.Settings;
        if (settings.RedactBeforeWorkflow) return true;
        var guardedKinds = new HashSet<WorkflowStepKind>();
        if (settings.RedactBeforeCopy) guardedKinds.Add(WorkflowStepKind.CopyImage);
        if (settings.RedactBeforeSave) guardedKinds.Add(WorkflowStepKind.SaveImage);
        if (settings.RedactBeforePin) guardedKinds.Add(WorkflowStepKind.PinImage);
        return await WorkflowGraphContainsStepKindAsync(workflow, guardedKinds, cancellationToken);
    }

    internal Task<CaptureAsset> PrepareWorkflowAssetAsync(
        CaptureAsset asset,
        bool redactWorkflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        return ApplyOutboundRedactionAsync(asset, redactWorkflow, "workflow", cancellationToken);
    }

    private async Task<bool> WorkflowGraphContainsStepKindAsync(
        CaptureWorkflow root,
        IReadOnlySet<WorkflowStepKind> kinds,
        CancellationToken cancellationToken)
    {
        if (kinds.Count == 0) return false;
        if (root.Steps.Any(step => step.IsEnabled != false && kinds.Contains(step.Kind))) return true;
        if (!root.Steps.Any(step => step.IsEnabled != false && step.Kind == WorkflowStepKind.RunWorkflow)) return false;

        var catalog = (await Services.Workflows.LoadAsync(cancellationToken))
            .ToDictionary(workflow => workflow.Id, StringComparer.Ordinal);
        var bestDepthByWorkflow = new Dictionary<string, int>(StringComparer.Ordinal);

        bool Visit(CaptureWorkflow workflow, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bestDepthByWorkflow.TryGetValue(workflow.Id, out var bestDepth) && bestDepth <= depth) return false;
            bestDepthByWorkflow[workflow.Id] = depth;
            foreach (var step in workflow.Steps.Where(step => step.IsEnabled != false))
            {
                if (kinds.Contains(step.Kind)) return true;
                if (step.Kind != WorkflowStepKind.RunWorkflow || string.IsNullOrWhiteSpace(step.Argument)) continue;
                if (depth >= WorkflowRuntimePolicy.MaximumSubworkflowDepth) continue;
                if (catalog.TryGetValue(step.Argument, out var child) && Visit(child, depth + 1)) return true;
            }
            return false;
        }

        return Visit(root, 1);
    }

    internal async Task RunWorkflowAsync(CaptureAsset asset, string workflowId, IReadOnlyDictionary<string, string>? initialVariables = null)
    {
        CaptureWorkflow? workflow = null;
        var executionStarted = false;
        try
        {
            workflow = (await Services.Workflows.LoadAsync()).FirstOrDefault(w => string.Equals(w.Id, workflowId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Workflow '{workflowId}' was not found.");
            var workflowAsset = await PrepareWorkflowAssetAsync(asset, workflow);
            executionStarted = true;
            var result = await Services.WorkflowExecutor.ExecuteAsync(
                workflow,
                CreateWorkflowExecutionContext(workflowAsset, initialVariables));
            await StoreWorkflowTraceBestEffortAsync(workflow, result, assetId: asset.Id);
            if (!result.Succeeded)
            {
                var failure = result.Steps.LastOrDefault(s => !s.Succeeded);
                ShowMainWindow();
                _mainWindow?.ShowStatus(failure?.Message ?? "Workflow stopped.", InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            if (executionStarted && workflow is not null)
                await StoreWorkflowFailureTraceBestEffortAsync(workflow, dryRun: false, assetId: asset.Id);
            Services.Log.Error("Workflow", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task CopyRecognizedTextAsync(CaptureAsset asset)
    {
        try
        {
            var result = await Services.Ocr.RecognizeAsync(asset.PngBytes, Services.Settings.PreferredOcrLanguage);
            Services.Clipboard.CopyText(result.Text);
            await Services.HistoryStore.UpdatePreviewsAsync(asset.Id, result.Text, null);
        }
        catch (Exception ex)
        {
            Services.Log.Error("DirectOcr", ex);
            OpenResult(asset, CaptureResultTab.Text);
        }
    }

    private void CopyCenterColor(CaptureAsset asset)
    {
        try
        {
            using var bitmap = BitmapCodec.Decode(asset.PngBytes);
            var pixel = bitmap.GetPixel(Math.Max(0, bitmap.Width / 2), Math.Max(0, bitmap.Height / 2));
            var color = ColorValue.FromRgb(pixel.R, pixel.G, pixel.B, pixel.A);
            Services.Clipboard.CopyText(color.Hex);
        }
        catch (Exception ex)
        {
            Services.Log.Error("DirectColor", ex);
        }
    }

    private bool EnsureFeature(ProductFeature feature)
    {
        if (Services.Entitlements.CanUse(feature)) return true;
        ShowMainWindow();
        _mainWindow?.ShowPlan(feature);
        return false;
    }

    internal void OpenResult(CaptureAsset asset, CaptureResultTab tab = CaptureResultTab.Preview)
    {
        var window = new CaptureResultWindow(asset, Services, tab);
        TrackWindow(window);
        window.Activate();
    }

    internal void OpenPin(CaptureAsset asset)
    {
        var pinCount = _childWindows.Count(window => window is PinWindow);
        if (pinCount >= 2 && !Services.Entitlements.CanUse(ProductFeature.UnlimitedPins))
        {
            ShowMainWindow();
            _mainWindow?.ShowPlan(ProductFeature.UnlimitedPins);
            return;
        }

        var window = new PinWindow(asset, Services, Services.Entitlements.CanUse(ProductFeature.PinClickThrough));
        TrackWindow(window);
        window.Activate();
    }

    private void RestorePinInteraction()
    {
        foreach (var pin in _childWindows.OfType<PinWindow>()) pin.SetClickThrough(false);
    }


    internal void ArrangePinsGrid()
    {
        var pins = _childWindows.OfType<PinWindow>().ToArray();
        if (pins.Length == 0) return;
        var firstWindow = WindowHelpers.GetAppWindow(pins[0]);
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(firstWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pins.Length)));
        var cellWidth = Math.Max(180, work.Width / columns);
        var rows = Math.Max(1, (int)Math.Ceiling(pins.Length / (double)columns));
        var cellHeight = Math.Max(120, work.Height / rows);
        for (var index = 0; index < pins.Length; index++)
        {
            var bounds = pins[index].GetWindowBounds();
            var column = index % columns;
            var row = index / columns;
            var x = work.X + column * cellWidth + Math.Max(0, (cellWidth - bounds.Width) / 2);
            var y = work.Y + row * cellHeight + Math.Max(0, (cellHeight - bounds.Height) / 2);
            pins[index].MovePin(x, y);
        }
    }

    internal void SnapPins()
    {
        const int snap = 24;
        var pins = _childWindows.OfType<PinWindow>().ToArray();
        if (pins.Length == 0) return;
        var placed = new List<PixelRect>();
        foreach (var pin in pins)
        {
            var bounds = pin.GetWindowBounds();
            var appWindow = WindowHelpers.GetAppWindow(pin);
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            var x = bounds.X; var y = bounds.Y;
            if (Math.Abs(bounds.X - work.X) <= snap) x = work.X;
            if (Math.Abs(bounds.Y - work.Y) <= snap) y = work.Y;
            if (Math.Abs(bounds.Right - (work.X + work.Width)) <= snap) x = work.X + work.Width - bounds.Width;
            if (Math.Abs(bounds.Bottom - (work.Y + work.Height)) <= snap) y = work.Y + work.Height - bounds.Height;
            foreach (var other in placed)
            {
                if (Math.Abs(x - other.Right) <= snap) x = other.Right;
                if (Math.Abs(x + bounds.Width - other.X) <= snap) x = other.X - bounds.Width;
                if (Math.Abs(y - other.Bottom) <= snap) y = other.Bottom;
                if (Math.Abs(y + bounds.Height - other.Y) <= snap) y = other.Y - bounds.Height;
            }
            pin.MovePin(x, y);
            placed.Add(new PixelRect(x, y, bounds.Width, bounds.Height));
        }
    }


    internal void OpenMagic(CaptureAsset asset)
    {
        if (!EnsureFeature(ProductFeature.MagicActions)) return;
        var window = new MagicActionWindow(asset, Services);
        TrackWindow(window);
        window.Activate();
    }

    internal bool AddToAiContext(CaptureAsset asset, string? label = null)
    {
        if (!EnsureFeature(ProductFeature.ContextStack)) return false;
        return Services.AiContext.TryAdd(asset, label);
    }

    internal void OpenAnnotation(CaptureAsset asset)
    {
        var window = new AnnotationWindow(asset, Services);
        TrackWindow(window);
        window.Activate();
    }

    internal void OpenRecoveredEditableProject(EditableProjectRecoveryItem item, EditableProjectPackage package)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(package);
        var window = new AnnotationWindow(package, Services, item.Journal.SessionId, item.Journal.DirtyRevision, item.Journal.OriginalProjectDisplayName);
        TrackWindow(window);
        window.Activate();
    }

    internal void OpenRecoveredDocumentationProject(DocumentationRecoveryItem item, DocumentationProjectPackage package)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(package);
        var window = new DocumentationWindow(package, Services, item.Journal.SessionId, item.Journal.DirtyRevision, item.Journal.DisplayName);
        TrackWindow(window);
        window.Activate();
    }

    internal void OpenRecoveredVideoEditProject(VideoEditRecoveryItem item, VideoEditProjectLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Project is null) throw new InvalidDataException("Recovered clip project did not contain a project model.");
        var window = new VideoEditorWindow(result, Services, item.Journal.SessionId, item.Journal.DirtyRevision, item.Journal.DisplayName);
        TrackWindow(window);
        window.Activate();
    }

    private async Task<bool> ReconcileSettingsReferencesAtStartupAsync()
    {
        var workflows = await Services.Workflows.LoadAsync(CancellationToken.None);
        var customActions = await Services.MagicActionStore.LoadAsync(CancellationToken.None);
        var validWorkflowIds = workflows.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var validMagicActionIds = BuiltInMagicActions.All.Select(item => item.Id)
            .Concat(customActions.Select(item => item.Id)).ToHashSet(StringComparer.Ordinal);

        await _settingsMutationGate.WaitAsync(CancellationToken.None);
        try
        {
            var current = Services.Settings;
            if (!SettingsReferencePolicy.RequiresExternalReferencePrune(current, validWorkflowIds, validMagicActionIds))
                return false;
            var reconciled = SettingsReferencePolicy.PruneExternalReferences(current, validWorkflowIds, validMagicActionIds);
            await Services.SettingsStore.SaveAsync(reconciled, CancellationToken.None);
            Services.CommitSettingsSnapshot(reconciled);
            Interlocked.Increment(ref _settingsRevision);
            return true;
        }
        finally
        {
            _settingsMutationGate.Release();
        }
    }

    internal async Task MutateSettingsAsync(
        Func<AppSettings, AppSettings> mutation,
        SettingsRuntimeEffects effects = SettingsRuntimeEffects.None,
        bool resetPersistence = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        AppSettings committed;
        long revision;
        await _settingsMutationGate.WaitAsync(cancellationToken);
        try
        {
            var previous = Services.Settings;
            var proposed = AppSettingsRules.NormalizeForRuntime(mutation(previous));
            var hotkeysChanged = !HotkeySettingsEquivalent(previous, proposed);
            if (hotkeysChanged && !TryApplyHotkeysForSettings(proposed))
                throw CreateHotkeyConfigurationException("One or more global hotkeys could not be registered. Settings were not changed.");

            try
            {
                if (resetPersistence)
                    await Services.SettingsStore.ResetAsync(proposed, cancellationToken);
                else
                    await Services.SettingsStore.SaveAsync(proposed, cancellationToken);
            }
            catch (Exception persistenceError)
            {
                if (hotkeysChanged && !TryApplyHotkeysForSettings(previous))
                    throw new AggregateException(
                        "Settings persistence failed and Windows also refused to restore the previous global-hotkey configuration.",
                        persistenceError,
                        CreateHotkeyConfigurationException("Hotkey rollback failed."));
                throw;
            }

            Services.CommitSettingsSnapshot(proposed);
            committed = proposed;
            revision = Interlocked.Increment(ref _settingsRevision);
            await ApplyCommittedSettingsEffectsUnsafeAsync(committed, effects);
        }
        finally
        {
            _settingsMutationGate.Release();
        }

        if (effects.HasFlag(SettingsRuntimeEffects.MainWindowUi) && revision == Volatile.Read(ref _settingsRevision))
            _mainWindow?.ApplySettingsToUi(committed, CombinedHotkeyError());
    }

    internal async Task<bool> TryMutateSettingsAsync(
        Func<AppSettings, AppSettings> mutation,
        SettingsRuntimeEffects effects = SettingsRuntimeEffects.None,
        bool resetPersistence = false,
        string logComponent = "SettingsMutation",
        CancellationToken cancellationToken = default)
    {
        try
        {
            await MutateSettingsAsync(mutation, effects, resetPersistence, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or AggregateException)
        {
            Services.Log.Error(logComponent, ex);
            return false;
        }
    }

    internal async Task<ConfigurationArchiveImportResult> ImportConfigurationAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await _settingsMutationGate.WaitAsync(cancellationToken);
        AppSettings effective;
        ConfigurationArchiveImportResult result;
        var warnings = new List<string>();
        long revision;
        try
        {
            var previous = Services.Settings;
            result = await Services.ConfigurationArchive.ImportAsync(sourcePath, cancellationToken);

            try
            {
                var desired = await ReloadSettingsFromStoreUnsafeAsync(CancellationToken.None);
                var workflows = await Services.Workflows.LoadAsync(CancellationToken.None);
                var customActions = await Services.MagicActionStore.LoadAsync(CancellationToken.None);
                var validWorkflowIds = workflows.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                var validMagicActionIds = BuiltInMagicActions.All.Select(item => item.Id)
                    .Concat(customActions.Select(item => item.Id)).ToHashSet(StringComparer.Ordinal);
                desired = SettingsReferencePolicy.PruneExternalReferences(desired, validWorkflowIds, validMagicActionIds);
                var previousPruned = SettingsReferencePolicy.PruneExternalReferences(previous, validWorkflowIds, validMagicActionIds);

                if (!TryApplyHotkeysForSettings(desired))
                {
                    var fallback = AppSettingsRules.NormalizeForRuntime(desired with
                    {
                        RegionHotkey = previousPruned.RegionHotkey,
                        RepeatHotkey = previousPruned.RepeatHotkey,
                        PersonalHotkeys = previousPruned.PersonalHotkeys
                    });
                    if (!TryApplyHotkeysForSettings(fallback))
                        throw CreateHotkeyConfigurationException("Imported settings conflicted with Windows hotkeys and the previous hotkeys could not be restored.");
                    effective = fallback;
                    warnings.Add("Imported hotkeys conflicted with Windows; the previously active hotkeys were kept.");
                }
                else effective = desired;

                try
                {
                    await Services.SettingsStore.SaveAsync(effective, CancellationToken.None);
                }
                catch (Exception saveError)
                {
                    if (!TryApplyHotkeysForSettings(previousPruned))
                        throw new AggregateException(
                            "Imported settings reconciliation could not be persisted and the previous hotkeys could not be restored.",
                            saveError,
                            CreateHotkeyConfigurationException("Hotkey rollback failed after configuration import."));
                    Services.CommitSettingsSnapshot(previousPruned);
                    Interlocked.Increment(ref _settingsRevision);
                    throw new InvalidOperationException(
                        "Configuration files were imported, but the reconciled settings could not be persisted. The previous runtime settings were retained; restart Magic Capture Desktop before making further settings changes.",
                        saveError);
                }

                var validProfileIds = effective.CaptureProfiles.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                try
                {
                    var disabled = await Services.WorkflowTriggers.DisableDanglingAsync(validWorkflowIds, validProfileIds, CancellationToken.None);
                    if (disabled > 0) warnings.Add($"Disabled {disabled} workflow trigger(s) whose imported workflow/profile target no longer exists.");
                }
                catch (Exception ex)
                {
                    Services.Log.Error("ConfigurationImportTriggerReconcile", ex);
                    warnings.Add("Workflow triggers could not be fully reconciled; review Automation Triggers before relying on unattended runs.");
                }

                Services.CommitSettingsSnapshot(effective);
                revision = Interlocked.Increment(ref _settingsRevision);
                await ApplyCommittedSettingsEffectsUnsafeAsync(effective, SettingsRuntimeEffects.Theme | SettingsRuntimeEffects.HistoryRetention | SettingsRuntimeEffects.WorkflowTriggers);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not AggregateException && ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "Configuration files were imported, but runtime reconciliation did not complete. Restart Magic Capture Desktop before editing settings so the imported files can be loaded consistently.", ex);
            }
        }
        finally
        {
            _settingsMutationGate.Release();
        }

        if (revision == Volatile.Read(ref _settingsRevision))
            _mainWindow?.ApplySettingsToUi(effective, CombinedHotkeyError());
        return result with { ImportedSettings = effective, Warning = warnings.Count == 0 ? null : string.Join(" ", warnings) };
    }

    private async Task<AppSettings> ReloadSettingsFromStoreUnsafeAsync(CancellationToken cancellationToken) =>
        AppSettingsRules.NormalizeForRuntime(await Services.SettingsStore.LoadStrictAsync(cancellationToken));

    private async Task ApplyCommittedSettingsEffectsUnsafeAsync(AppSettings settings, SettingsRuntimeEffects effects)
    {
        if (effects.HasFlag(SettingsRuntimeEffects.Theme))
        {
            try
            {
                ApplyTheme(_mainWindow, settings.Theme);
                foreach (var window in _childWindows.ToArray()) ApplyTheme(window, settings.Theme);
            }
            catch (Exception ex) { Services.Log.Error("SettingsThemePostCommit", ex); }
        }
        if (effects.HasFlag(SettingsRuntimeEffects.HistoryRetention))
        {
            try { await Services.HistoryStore.ApplyRetentionAsync(settings, CancellationToken.None); }
            catch (Exception ex) { Services.Log.Error("SettingsRetentionPostCommit", ex); }
        }
        if (effects.HasFlag(SettingsRuntimeEffects.WorkflowTriggers))
        {
            try { await Services.WorkflowTriggerEngine.ReloadAsync(CancellationToken.None); }
            catch (Exception ex) { Services.Log.Error("SettingsTriggerPostCommit", ex); }
        }
    }

    private static bool HotkeySettingsEquivalent(AppSettings left, AppSettings right) =>
        left.RegionHotkey == right.RegionHotkey &&
        left.RepeatHotkey == right.RepeatHotkey &&
        left.PersonalHotkeys.SequenceEqual(right.PersonalHotkeys);

    private bool TryApplyHotkeysForSettings(AppSettings settings) =>
        Services.Hotkeys.TryApplyConfiguration(
            settings.RegionHotkey,
            settings.RepeatHotkey,
            settings.PersonalHotkeys,
            Services.Entitlements.CanUse(ProductFeature.ProRepeatHotkey));

    private InvalidOperationException CreateHotkeyConfigurationException(string prefix)
    {
        var details = CombinedHotkeyError();
        return new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details}");
    }

    private string? CombinedHotkeyError()
    {
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(Services.Hotkeys.LastRegistrationError)) errors.Add(Services.Hotkeys.LastRegistrationError);
        if (!string.IsNullOrWhiteSpace(Services.Hotkeys.LastRepeatRegistrationError)) errors.Add(Services.Hotkeys.LastRepeatRegistrationError);
        errors.AddRange(Services.Hotkeys.PersonalRegistrationErrors.Values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!Services.Hotkeys.LastRollbackSucceeded && !string.IsNullOrWhiteSpace(Services.Hotkeys.LastRollbackError)) errors.Add(Services.Hotkeys.LastRollbackError);
        return errors.Count == 0 ? null : string.Join(" ", errors.Distinct(StringComparer.Ordinal));
    }

    private async Task DispatchPersonalHotkeyAsync(PersonalHotkeyBinding binding)
    {
        try
        {
            switch (binding.Kind)
            {
                case PersonalHotkeyKind.Capture:
                    if (binding.Target.StartsWith("profile:", StringComparison.OrdinalIgnoreCase))
                    {
                        var profileId = binding.Target["profile:".Length..];
                        var profile = Services.Settings.CaptureProfiles.FirstOrDefault(item =>
                            string.Equals(item.Id, profileId, StringComparison.Ordinal));
                        if (profile is null) throw new InvalidOperationException($"Capture profile not found: {profileId}");
                        await RunCaptureProfileAsync(profile);
                    }
                    else if (Enum.TryParse<CaptureHotkeyAction>(binding.Target, true, out var capture))
                    {
                        switch (capture)
                        {
                            case CaptureHotkeyAction.Region: await CaptureRegionFromUiAsync(); break;
                            case CaptureHotkeyAction.ForegroundWindow: await CaptureForegroundWindowAsync(); break;
                            case CaptureHotkeyAction.ActiveMonitor: await CaptureActiveMonitorAsync(); break;
                            case CaptureHotkeyAction.VirtualDesktop: await CaptureVirtualDesktopAsync(); break;
                            case CaptureHotkeyAction.RepeatRegion: await CaptureRepeatRegionAsync(); break;
                        }
                    }
                    break;
                case PersonalHotkeyKind.Workflow:
                    await CaptureRegionForWorkflowAsync(binding.Target);
                    break;
                case PersonalHotkeyKind.MagicAction:
                    if (!EnsureFeature(ProductFeature.MagicActions)) return;
                    var magicResult = await Services.Capture.CaptureRegionAsync(
                        OverlayCaptureAction.Magic,
                        Services.Settings.CaptureCursor,
                        Services.Entitlements.Current.Tier,
                        overlayTheme: Services.Settings.CaptureOverlayTheme,
                        actionLayout: Services.Settings.OverlayActions);
                    if (magicResult is null) return;
                    await RememberRegionAsync(magicResult.SelectionBounds);
                    if (magicResult.Action == OverlayCaptureAction.Magic)
                    {
                        foreach (var asset in magicResult.Assets)
                        {
                            var window = new MagicActionWindow(asset, Services, binding.Target);
                            TrackWindow(window);
                            window.Activate();
                        }
                    }
                    else await HandleCaptureRequestAsync(magicResult);
                    break;
                case PersonalHotkeyKind.Editor:
                    await OpenLastHistoryItemInEditorAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error("PersonalHotkey", ex);
            ShowMainWindow();
            _mainWindow?.ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async Task OpenLastHistoryItemInEditorAsync()
    {
        var item = (await Services.HistoryStore.ListAsync()).FirstOrDefault();
        if (item is null)
        {
            ShowMainWindow();
            _mainWindow?.ShowStatus("History is empty; there is no capture to open in the editor.", InfoBarSeverity.Informational);
            return;
        }
        var bytes = await Imaging.ImageFileReader.ReadAsync(Services.HistoryStore.GetAbsolutePath(item));
        var kind = Enum.TryParse<CaptureSourceKind>(item.SourceKind, out var parsed) ? parsed : CaptureSourceKind.Region;
        var asset = new CaptureAsset(item.Id, item.CreatedUtc, new PixelRect(0, 0, item.Width, item.Height), bytes, item.Width, item.Height,
            kind, item.SourceDisplayName ?? "History", item.WindowTitle, item.ProcessName, item.MonitorName, ExecutablePath: item.ExecutablePath);
        OpenAnnotation(asset);
    }

    internal async Task<StorePurchaseOutcome> PurchaseProAsync() => await Services.Entitlements.PurchaseProAsync();

    private static void ApplyTheme(Window? window, AppTheme theme)
    {
        if (window?.Content is not FrameworkElement root) return;
        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    internal void TrackChildWindow(Window window) => TrackWindow(window);

    private void TrackWindow(Window window)
    {
        ApplyTheme(window, Services.Settings.Theme);
        _childWindows.Add(window);
        window.Closed += (_, _) => _childWindows.Remove(window);
    }

    internal void MainWindowShowAiSettings()
    {
        ShowMainWindow();
        _mainWindow?.ShowAiSettings();
    }

    internal void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Activate();
        WindowHelpers.GetAppWindow(_mainWindow).Show();
        _ = RefreshEntitlementAndNoticeAsync();
    }

    private async Task RefreshEntitlementAndNoticeAsync()
    {
        try
        {
            await Services.Entitlements.RefreshAsync();
            if (Services.Entitlements.ShouldShowTrialExpiredNotice && _mainWindow is not null)
                await _mainWindow.ShowTrialExpiredAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("EntitlementRefresh", ex);
        }
    }

    internal void HideMainWindow()
    {
        if (_mainWindow is null) return;
        WindowHelpers.GetAppWindow(_mainWindow).Hide();
    }

    internal void ExitFromMainWindowClosing() => HideMainWindow();

    internal void ExitApplication()
    {
        _exitRequested = true;
        Services.Recording.Stop();
        Services.StepRecorder.StopAsync().GetAwaiter().GetResult();
        Services.WorkflowTriggerEngine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Services.CaptureWatch.Dispose();
        Services.Hotkeys.Dispose();
        Services.Tray.Dispose();
        Services.MessageRouter.Dispose();
        _singleInstance?.Dispose();
        _singleInstance = null;
        foreach (var child in _childWindows.ToArray()) child.Close();
        _mainWindow?.Close();
        Exit();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (_services is not null) _services.Log.Error("Unhandled", e.Exception);
        // Do not keep a long-running tray process alive after failures that can leave memory or
        // native state corrupted. Ordinary UI/event exceptions are logged and isolated.
        e.Handled = !FatalExceptionPolicy.IsFatal(e.Exception);
    }
}
