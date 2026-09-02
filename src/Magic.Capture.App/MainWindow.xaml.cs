using System.Text.Json;
using System.Diagnostics;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Ai;
using Magic.Capture.App.Ai.Provider;
using Magic.Capture.App.Commerce;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Workflows;
using Magic.Capture.App.Destinations;
using Magic.Capture.App.Documentation;
using Magic.Capture.App.LocalActions;
using Magic.Capture.App.Utilities;
using Magic.Capture.App.VideoEditing;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Persistence;
using Magic.Capture.App.Recording;
using Magic.Capture.App.ViewModels;
using Magic.Capture.App.Views;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Export;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.History;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Workflows;
using Magic.Capture.Core.Destinations;
using Magic.Capture.Core.LocalActions;
using Magic.Capture.Core.Utilities;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Privacy;
using Magic.Capture.Core.Recording;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace Magic.Capture.App;

public sealed partial class MainWindow : Window
{
    private ApplicationServices? _services;
    private readonly List<byte[]> _stitchFrames = [];
    private readonly List<string> _stitchNames = [];
    private byte[]? _compareA;
    private byte[]? _compareB;
    private string? _compareAName;
    private string? _compareBName;
    private bool _updatingStartupToggle;
    private IReadOnlyList<HistoryDisplayItem> _historyDisplayItems = Array.Empty<HistoryDisplayItem>();
    private CancellationTokenSource? _historyRefreshCts;
    private long _historyRefreshGeneration;
    private CancellationTokenSource? _historySearchCts;
    private long _historySearchGeneration;
    private HistoryQueryOptions _historyQueryOptions = new();
    private HistoryLibrarySnapshot _historyLibrarySnapshot = HistoryLibrarySnapshot.Empty;
    private bool _updatingHistoryOrganization;
    private const int MaximumDroppedFiles = 500;
    private IReadOnlyList<AiProviderProfile> _aiProfiles = Array.Empty<AiProviderProfile>();
    private IReadOnlyList<MagicActionDefinition> _customActions = Array.Empty<MagicActionDefinition>();
    private IReadOnlyList<MagicRecipe> _magicRecipes = Array.Empty<MagicRecipe>();
    private string? _editingMagicRecipeId;
    private bool _updatingAiProfile;
    private IReadOnlyList<CaptureWorkflow> _workflowItems = Array.Empty<CaptureWorkflow>();
    private string? _editingWorkflowId;
    private IReadOnlyList<CustomHttpDestination> _destinationItems = Array.Empty<CustomHttpDestination>();
    private IReadOnlyList<LocalActionProfile> _localActionItems = Array.Empty<LocalActionProfile>();
    private EditableProjectRecoveryItem? _editableProjectRecoveryItem;
    private readonly HashSet<Guid> _ignoredEditableProjectRecoverySessions = [];
    private DocumentationRecoveryItem? _documentationRecoveryItem;
    private readonly HashSet<Guid> _ignoredDocumentationRecoverySessions = [];
    private VideoEditRecoveryItem? _videoEditRecoveryItem;
    private readonly HashSet<Guid> _ignoredVideoEditRecoverySessions = [];
    private string? _editingLocalActionId;
    private bool _localActionsLoadHealthy;
    private bool _destinationsLoadHealthy;
    private ImageEffectPipeline _lastEffectPipeline = ImageEffectPresets.BuiltIn[0].Pipeline;
    private Guid? _editingDestinationId;
    private bool _recordingCaptureExclusionApplied;
    private sealed record WatchWorkflowOption(string Name, string? WorkflowId, ProductTier RequiredTier);
    private sealed record CaptureProfileWorkflowOption(string Name, string? WorkflowId)
    {
        public override string ToString() => Name;
    }
    private sealed record RecentRegionOption(PixelRect Bounds)
    {
        public string Label => $"{Bounds.Width}×{Bounds.Height} @ {Bounds.X},{Bounds.Y}";
    }

    private sealed record HistoryLibraryOption(string Name, string? Id)
    {
        public override string ToString() => Name;
    }
    private sealed record HistoryTimelineRow(string DayLabel, HistoryDisplayItem Display);
    private sealed record HistorySessionOption(HistorySessionSummary Summary)
    {
        public override string ToString() => $"{Summary.LastCaptureUtc.LocalDateTime:g} · {Summary.CaptureCount} capture(s) · {Summary.SessionId}";
    }
    private sealed class WorkflowBuilderStepView
    {
        public WorkflowBuilderStepView(WorkflowStep step) => Step = step;
        public WorkflowStep Step { get; }
        public string Display => $"{(Step.IsEnabled == false ? "○" : "●")} {Step.Kind} · {Step.Id}"
            + (string.IsNullOrWhiteSpace(Step.Argument) ? string.Empty : $" · {Step.Argument}")
            + (string.IsNullOrWhiteSpace(Step.Condition) ? string.Empty : $" · if {Step.Condition}");
    }

    private sealed class WorkflowBuilderParameterView
    {
        public WorkflowBuilderParameterView(WorkflowParameterDefinition definition) => Definition = definition;
        public WorkflowParameterDefinition Definition { get; }
        public string Display => $"{Definition.Name} · {Definition.Kind}"
            + (Definition.Required ? " · required" : string.Empty)
            + (Definition.Kind == WorkflowParameterKind.Choice ? $" · {Definition.Choices?.Count ?? 0} choices" : string.Empty);
    }

    private sealed class WorkflowTraceView
    {
        public WorkflowTraceView(WorkflowTraceRecord record) => Record = record;
        public WorkflowTraceRecord Record { get; }
        public string Display => $"{Record.StartedUtc.LocalDateTime:g} · {Record.WorkflowName} · "
            + (Record.DryRun ? "dry-run" : Record.Succeeded ? "success" : "failed")
            + (Record.ResumedFromTraceId is null ? string.Empty : " · resumed");
    }

    public MainWindow()
    {
        InitializeComponent();
        Platform.WindowHelpers.GetAppWindow(this).Closing += MainAppWindow_Closing;
    }

    internal void AttachServices(ApplicationServices services)
    {
        _services = services;
        OcrLanguageCombo.ItemsSource = services.Ocr.AvailableLanguageTags;
        _ = RefreshHistoryAsync();
        _ = RefreshStorePriceAsync();
        AiProviderKindCombo.ItemsSource = Enum.GetValues<AiProviderKind>();
        WorkflowBuilderAddKindCombo.ItemsSource = Enum.GetValues<WorkflowStepKind>();
        WorkflowBuilderAddKindCombo.SelectedIndex = 0;
        WorkflowBuilderParameterKindCombo.ItemsSource = Enum.GetValues<WorkflowParameterKind>();
        WorkflowBuilderParameterKindCombo.SelectedIndex = 0;
        ResetWorkflowBuilder();
        _ = RefreshAiSettingsAsync();
        _ = RefreshWorkflowsAsync();
        _ = RefreshWorkflowTracesAsync();
        _ = RefreshLocalActionsAsync();
        _ = RefreshDestinationsAsync();
        services.Recording.ProgressChanged += Recording_ProgressChanged;
        _ = InitializeRecordingUiAsync();
        _ = RefreshEditableProjectRecoveryAsync();
        _ = RefreshDocumentationRecoveryAsync();
        _ = RefreshVideoEditRecoveryAsync();
    }

    private ApplicationServices Services => _services ?? throw new InvalidOperationException("Services are not attached.");

    private void MainAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if ((Application.Current as App)?.IsExitRequested == true) return;
        args.Cancel = true;
        (Application.Current as App)?.HideMainWindow();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "home";
        if (tag == "stitch" && !Services.Entitlements.CanUse(ProductFeature.ScrollingStitch))
        {
            ShowPlan(ProductFeature.ScrollingStitch);
            return;
        }
        if (tag == "compare" && !Services.Entitlements.CanUse(ProductFeature.CompareWorkspace))
        {
            ShowPlan(ProductFeature.CompareWorkspace);
            return;
        }
        if (tag == "destinations" && !Services.Entitlements.CanUse(ProductFeature.CustomDestinations))
        {
            ShowPlan(ProductFeature.CustomDestinations);
            return;
        }
        ShowPage(tag);
        if (tag == "history") _ = RefreshHistoryAsync();
        if (tag == "workflows") { _ = RefreshWorkflowsAsync(); _ = RefreshLocalActionsAsync(); }
        if (tag == "destinations") _ = RefreshDestinationsAsync();
    }

    private void ShowPage(string tag)
    {
        HomePage.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = tag == "history" ? Visibility.Visible : Visibility.Collapsed;
        WorkflowsPage.Visibility = tag == "workflows" ? Visibility.Visible : Visibility.Collapsed;
        UtilitiesPage.Visibility = tag == "utilities" ? Visibility.Visible : Visibility.Collapsed;
        DestinationsPage.Visibility = tag == "destinations" ? Visibility.Visible : Visibility.Collapsed;
        StitchPage.Visibility = tag == "stitch" ? Visibility.Visible : Visibility.Collapsed;
        ComparePage.Visibility = tag == "compare" ? Visibility.Visible : Visibility.Collapsed;
        AiPage.Visibility = tag == "ai" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PlanPage.Visibility = tag == "plan" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private NavigationViewItem? FindNavItem(string tag) =>
        Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()
            .FirstOrDefault(x => string.Equals(x.Tag as string, tag, StringComparison.Ordinal));

    private void SelectPage(string tag)
    {
        var item = FindNavItem(tag);
        if (item is not null) Nav.SelectedItem = item;
        ShowPage(tag);
    }

    internal void ShowSettings() => SelectPage("settings");
    internal void ShowAiSettings() { SelectPage("ai"); _ = RefreshAiSettingsAsync(); }
    internal void ShowHistory() { SelectPage("history"); _ = RefreshHistoryAsync(); }
    internal void ShowWorkflows() { SelectPage("workflows"); _ = RefreshWorkflowsAsync(); _ = RefreshLocalActionsAsync(); }
    internal void ShowUtilities() => SelectPage("utilities");
    internal void ShowPlan() => ShowPlan(null);

    internal void ShowPlan(ProductFeature? feature)
    {
        SelectPage("plan");
        if (feature is null)
        {
            PlanFeatureInfo.IsOpen = false;
            return;
        }

        var selectedFeature = feature.Value;
        var required = FeatureCatalog.RequiredTier(selectedFeature);
        PlanFeatureInfo.Title = required == ProductTier.ProLifetime ? "Pro feature" : "Plus feature";
        PlanFeatureInfo.Message = required == ProductTier.ProLifetime
            ? $"{FriendlyFeature(selectedFeature)} is included with Magic Capture Desktop Pro Lifetime."
            : $"{FriendlyFeature(selectedFeature)} is available during the 7-day Plus trial and in Pro Lifetime.";
        PlanFeatureInfo.IsOpen = true;
    }

    internal void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        GlobalInfoBar.Title = "Magic Capture Desktop";
        GlobalInfoBar.Message = message;
        GlobalInfoBar.Severity = severity;
        GlobalInfoBar.IsOpen = true;
    }

    private static string FriendlyFeature(ProductFeature feature) => feature switch
    {
        ProductFeature.TableExtraction => "Table extraction",
        ProductFeature.BarcodeRecognition => "QR and barcode recognition",
        ProductFeature.ScrollingStitch => "Vertical stitch",
        ProductFeature.AdvancedEditor => "Advanced editor tools",
        ProductFeature.UnlimitedPins => "Unlimited pins",
        ProductFeature.RepeatLastRegion => "Repeat last region",
        ProductFeature.FixedAspectCapture => "Fixed-aspect capture",
        ProductFeature.CompareWorkspace => "Compare workspace",
        ProductFeature.PinClickThrough => "Click-through pins",
        ProductFeature.UnlimitedHistory => "Unlimited history options",
        ProductFeature.AiProviders => "AI provider integration",
        ProductFeature.MagicActions => "Magic Actions",
        ProductFeature.ContextStack => "AI Context Stack",
        ProductFeature.EvidenceAnchoring => "Evidence anchoring",
        ProductFeature.SemanticCompare => "Semantic Compare",
        ProductFeature.CustomMagicActions => "Custom Magic Actions",
        ProductFeature.BasicWorkflows => "Capture workflows",
        ProductFeature.AutoCapture => "Auto Capture",
        ProductFeature.AdvancedWorkflows => "Advanced workflows",
        ProductFeature.ChangeAwareCaptureWatch => "Change-aware Capture Watch",
        ProductFeature.UtilityMetadataAndHashes => "Metadata and hash utilities",
        ProductFeature.UtilityImagePack => "Image utility pack",
        ProductFeature.CustomDestinations => "Custom destinations",
        ProductFeature.AiGuard => "AI Guard",
        ProductFeature.AiResultCache => "AI result cache",
        ProductFeature.MagicRecipes => "Magic Recipes",
        _ => feature.ToString()
    };

    private async Task RefreshEditableProjectRecoveryAsync()
    {
        if (_services is null) return;
        try
        {
            var candidates = await Services.EditableProjectRecovery.ListAsync();
            _editableProjectRecoveryItem = candidates.FirstOrDefault(item => !_ignoredEditableProjectRecoverySessions.Contains(item.Journal.SessionId));
            if (_editableProjectRecoveryItem is null)
            {
                EditableProjectRecoveryCard.Visibility = Visibility.Collapsed;
                EditableProjectRecoveryText.Text = string.Empty;
                return;
            }

            var journal = _editableProjectRecoveryItem.Journal;
            var name = string.IsNullOrWhiteSpace(journal.OriginalProjectDisplayName) ? "an unsaved editor project" : journal.OriginalProjectDisplayName;
            EditableProjectRecoveryText.Text = $"{name} · autosaved {journal.UpdatedUtc.LocalDateTime:g}. Recovering opens a copy and never overwrites an existing project file.";
            EditableProjectRecoveryCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _editableProjectRecoveryItem = null;
            EditableProjectRecoveryCard.Visibility = Visibility.Collapsed;
            Services.Log.Error("EditableProjectRecoveryList", ex);
        }
    }

    private async void RecoverEditableProject_Click(object sender, RoutedEventArgs e)
    {
        var item = _editableProjectRecoveryItem;
        if (item is null) return;
        try
        {
            var package = await Services.EditableProjectRecovery.LoadAsync(item);
            (Application.Current as App)?.OpenRecoveredEditableProject(item, package);
            _ignoredEditableProjectRecoverySessions.Add(item.Journal.SessionId);
            _editableProjectRecoveryItem = null;
            ShowStatus("Recovered the local editor autosave. Save it when you are ready; the original project file was not overwritten.", InfoBarSeverity.Success);
            await RefreshEditableProjectRecoveryAsync();
        }
        catch (Exception ex) when (IsInvalidRecoveryCandidate(ex))
        {
            Services.Log.Error("EditableProjectRecoveryInvalid", ex);
            try
            {
                await Services.EditableProjectRecovery.DeleteAsync(item.Journal.SessionId);
            }
            catch (Exception cleanupEx)
            {
                Services.Log.Error("EditableProjectRecoveryInvalidCleanup", cleanupEx);
            }
            _editableProjectRecoveryItem = null;
            ShowStatus("That autosave was invalid and was quarantined. Existing project files were left unchanged.", InfoBarSeverity.Warning);
            await RefreshEditableProjectRecoveryAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("EditableProjectRecoveryOpen", ex);
            _editableProjectRecoveryItem = null;
            ShowStatus("The autosave could not be opened right now, so it was kept for another attempt.", InfoBarSeverity.Warning);
            await RefreshEditableProjectRecoveryAsync();
        }
    }

    private static bool IsInvalidRecoveryCandidate(Exception ex) =>
        ex is InvalidDataException or JsonException;

    private async void DiscardEditableProjectRecovery_Click(object sender, RoutedEventArgs e)
    {
        var item = _editableProjectRecoveryItem;
        if (item is null) return;
        try
        {
            await Services.EditableProjectRecovery.DeleteAsync(item.Journal.SessionId);
            _editableProjectRecoveryItem = null;
            ShowStatus("Discarded the local editor autosave.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            Services.Log.Error("EditableProjectRecoveryDiscard", ex);
            ShowStatus("The autosave could not be removed. You can try again after restarting Magic Capture Desktop.", InfoBarSeverity.Warning);
        }
        await RefreshEditableProjectRecoveryAsync();
    }

    private async Task RefreshDocumentationRecoveryAsync()
    {
        if (_services is null) return;
        try
        {
            var candidates = await Services.DocumentationRecovery.ListAsync();
            _documentationRecoveryItem = candidates.FirstOrDefault(item => !_ignoredDocumentationRecoverySessions.Contains(item.Journal.SessionId));
            if (_documentationRecoveryItem is null)
            {
                DocumentationRecoveryCard.Visibility = Visibility.Collapsed;
                DocumentationRecoveryText.Text = string.Empty;
                return;
            }

            var journal = _documentationRecoveryItem.Journal;
            var name = string.IsNullOrWhiteSpace(journal.DisplayName) ? "an unsaved documentation project" : journal.DisplayName;
            DocumentationRecoveryText.Text = $"{name} · autosaved {journal.UpdatedUtc.LocalDateTime:g}. Recovering opens a copy and never overwrites an existing .magicdoc file.";
            DocumentationRecoveryCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _documentationRecoveryItem = null;
            DocumentationRecoveryCard.Visibility = Visibility.Collapsed;
            Services.Log.Error("DocumentationRecoveryList", ex);
        }
    }

    private async void RecoverDocumentationProject_Click(object sender, RoutedEventArgs e)
    {
        var item = _documentationRecoveryItem;
        if (item is null) return;
        try
        {
            var package = await Services.DocumentationRecovery.LoadAsync(item);
            (Application.Current as App)?.OpenRecoveredDocumentationProject(item, package);
            _ignoredDocumentationRecoverySessions.Add(item.Journal.SessionId);
            _documentationRecoveryItem = null;
            ShowStatus("Recovered the local documentation autosave. Save it when ready; the original project file was not overwritten.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (IsInvalidRecoveryCandidate(ex))
        {
            Services.Log.Error("DocumentationRecoveryInvalid", ex);
            try { await Services.DocumentationRecovery.DeleteAsync(item.Journal.SessionId); }
            catch (Exception cleanupEx) { Services.Log.Error("DocumentationRecoveryInvalidCleanup", cleanupEx); }
            _documentationRecoveryItem = null;
            ShowStatus("That documentation autosave was invalid and was quarantined. Existing project files were left unchanged.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("DocumentationRecoveryOpen", ex);
            _documentationRecoveryItem = null;
            ShowStatus("The documentation autosave could not be opened right now, so it was kept for another attempt.", InfoBarSeverity.Warning);
        }
        await RefreshDocumentationRecoveryAsync();
    }

    private async void DiscardDocumentationRecovery_Click(object sender, RoutedEventArgs e)
    {
        var item = _documentationRecoveryItem;
        if (item is null) return;
        try
        {
            await Services.DocumentationRecovery.DeleteAsync(item.Journal.SessionId);
            _documentationRecoveryItem = null;
            ShowStatus("Discarded the local documentation autosave.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            Services.Log.Error("DocumentationRecoveryDiscard", ex);
            ShowStatus("The documentation autosave could not be removed. You can try again after restarting Magic Capture Desktop.", InfoBarSeverity.Warning);
        }
        await RefreshDocumentationRecoveryAsync();
    }

    private async Task RefreshVideoEditRecoveryAsync()
    {
        if (_services is null) return;
        try
        {
            var candidates = await Services.VideoEditRecovery.ListAsync();
            _videoEditRecoveryItem = candidates.FirstOrDefault(item => !_ignoredVideoEditRecoverySessions.Contains(item.Journal.SessionId));
            if (_videoEditRecoveryItem is null)
            {
                VideoEditRecoveryCard.Visibility = Visibility.Collapsed;
                VideoEditRecoveryText.Text = string.Empty;
                return;
            }

            var journal = _videoEditRecoveryItem.Journal;
            var name = string.IsNullOrWhiteSpace(journal.DisplayName) ? "an unsaved video edit" : journal.DisplayName;
            VideoEditRecoveryText.Text = $"{name} · autosaved {journal.UpdatedUtc.LocalDateTime:g}. Recovering opens a copy and never overwrites an existing .magicclip file.";
            VideoEditRecoveryCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _videoEditRecoveryItem = null;
            VideoEditRecoveryCard.Visibility = Visibility.Collapsed;
            Services.Log.Error("VideoEditRecoveryList", ex);
        }
    }

    private async void RecoverVideoEditProject_Click(object sender, RoutedEventArgs e)
    {
        var item = _videoEditRecoveryItem;
        if (item is null) return;
        try
        {
            var result = await Services.VideoEditRecovery.LoadAsync(item);
            if (result.Project is null) throw new InvalidDataException("Recovered clip project did not contain a project model.");
            (Application.Current as App)?.OpenRecoveredVideoEditProject(item, result);
            _ignoredVideoEditRecoverySessions.Add(item.Journal.SessionId);
            _videoEditRecoveryItem = null;
            ShowStatus("Recovered the local video-edit autosave. Save it when ready; the original project file was not overwritten.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (IsInvalidRecoveryCandidate(ex))
        {
            Services.Log.Error("VideoEditRecoveryInvalid", ex);
            try { await Services.VideoEditRecovery.DeleteAsync(item.Journal.SessionId); }
            catch (Exception cleanupEx) { Services.Log.Error("VideoEditRecoveryInvalidCleanup", cleanupEx); }
            _videoEditRecoveryItem = null;
            ShowStatus("That video-edit autosave was invalid and was quarantined. Existing project files were left unchanged.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("VideoEditRecoveryOpen", ex);
            _videoEditRecoveryItem = null;
            ShowStatus("The video-edit autosave could not be opened right now, so it was kept for another attempt.", InfoBarSeverity.Warning);
        }
        await RefreshVideoEditRecoveryAsync();
    }

    private async void DiscardVideoEditRecovery_Click(object sender, RoutedEventArgs e)
    {
        var item = _videoEditRecoveryItem;
        if (item is null) return;
        try
        {
            await Services.VideoEditRecovery.DeleteAsync(item.Journal.SessionId);
            _videoEditRecoveryItem = null;
            ShowStatus("Discarded the local video-edit autosave.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            Services.Log.Error("VideoEditRecoveryDiscard", ex);
            ShowStatus("The video-edit autosave could not be removed. You can try again after restarting Magic Capture Desktop.", InfoBarSeverity.Warning);
        }
        await RefreshVideoEditRecoveryAsync();
    }

    private async Task RefreshWorkflowsAsync()
    {
        if (_services is null) return;
        try
        {
            _workflowItems = await Services.Workflows.LoadAsync();
            WorkflowList.ItemsSource = _workflowItems;
            if (WorkflowList.SelectedItem is null && _workflowItems.Count > 0) WorkflowList.SelectedItem = _workflowItems[0];
            var selectedWatch = (WatchWorkflowCombo.SelectedItem as WatchWorkflowOption)?.WorkflowId;
            var watchItems = new List<WatchWorkflowOption> { new("History only", null, ProductTier.Free) };
            watchItems.AddRange(_workflowItems.Select(workflow => new WatchWorkflowOption(workflow.Name, workflow.Id, workflow.RequiredTier)));
            WatchWorkflowCombo.ItemsSource = watchItems;
            WatchWorkflowCombo.SelectedItem = watchItems.FirstOrDefault(item => item.WorkflowId == selectedWatch) ?? watchItems[0];
        }
        catch (Exception ex)
        {
            if (_workflowItems.Count == 0) _workflowItems = WorkflowCatalog.BuiltIns;
            WorkflowList.ItemsSource = _workflowItems;
            WorkflowStatusText.Text = "Custom workflows could not be loaded. Built-in workflows remain available; the custom file was not treated as empty.";
            Services.Log.Error("WorkflowLoad", ex);
        }
    }

    private void WorkflowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowNameText.Text = "Select a workflow";
            WorkflowDescriptionText.Text = string.Empty;
            WorkflowTierText.Text = string.Empty;
            WorkflowStepList.ItemsSource = null;
            return;
        }
        WorkflowNameText.Text = workflow.Name;
        WorkflowDescriptionText.Text = workflow.Description;
        WorkflowTierText.Text = $"Required tier: {workflow.RequiredTier} · schema {workflow.SchemaVersion} · {workflow.Parameters?.Count ?? 0} parameter(s)";
        WorkflowStepList.ItemsSource = workflow.Steps.Select(step => (step.IsEnabled == false ? "○ " : "● ") + $"{step.Kind}" + (string.IsNullOrWhiteSpace(step.Argument) ? string.Empty : $" · {step.Argument}")).ToArray();
    }

    private async void RefreshWorkflows_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWorkflowsAsync();
        await RefreshWorkflowTracesAsync();
        await RefreshLocalActionsAsync();
    }

    private async void RunWorkflowOnHistory_Click(object sender, RoutedEventArgs e)
    {
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowStatusText.Text = "Select a workflow first.";
            return;
        }
        var selections = SelectedHistoryDisplays(WorkflowRuntimePolicy.MaximumBatchAssets);
        if (selections.Count == 0)
        {
            WorkflowStatusText.Text = "Select one or more captures in History first.";
            return;
        }

        try
        {
            var app = (App)Application.Current;
            var redactWorkflow = await app.ShouldRedactWorkflowAsync(workflow);
            var loaders = selections
                .Select(selection => (Func<CancellationToken, Task<CaptureAsset?>>)(async cancellationToken =>
                {
                    var loaded = await LoadHistoryAssetAsync(selection, cancellationToken);
                    return loaded is null ? null : await app.PrepareWorkflowAssetAsync(loaded, redactWorkflow, cancellationToken);
                }))
                .ToArray();
            WorkflowStatusText.Text = $"Running {workflow.Name} sequentially on {selections.Count} capture(s)…";
            var summary = await Services.WorkflowBatchRunner.ExecuteAsync(
                workflow,
                loaders,
                asset => app.CreateWorkflowExecutionContext(asset));
            WorkflowStatusText.Text = $"Batch complete · {summary.Completed} succeeded · {summary.Failed} failed · {summary.Requested} requested.";
            await RefreshWorkflowTracesAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowHistoryBatch", ex);
            WorkflowStatusText.Text = ex.Message;
        }
    }

    private async void RunWorkflowLoopOnHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowStatusText.Text = "Select a workflow first.";
            return;
        }

        Guid? traceAssetId = null;
        var executionStarted = false;
        try
        {
            var assets = await SelectedHistoryAssetsAsync(WorkflowRuntimePolicy.MaximumLoopImages);
            if (assets.Count == 0)
            {
                WorkflowStatusText.Text = "Select one or more History captures for the image loop.";
                return;
            }
            var app = (App)Application.Current;
            var redactWorkflow = await app.ShouldRedactWorkflowAsync(workflow);
            var prepared = new List<CaptureAsset>(assets.Count);
            foreach (var asset in assets)
                prepared.Add(await app.PrepareWorkflowAssetAsync(asset, redactWorkflow));

            var primary = prepared[0];
            traceAssetId = primary.Id;
            await RecordHistoryWorkflowStartBestEffortAsync(prepared.Select(item => item.Id), workflow);
            WorkflowStatusText.Text = $"Running {workflow.Name} once with {prepared.Count} image(s) in loop context…";
            executionStarted = true;
            var result = await Services.WorkflowExecutor.ExecuteAsync(
                workflow,
                app.CreateWorkflowExecutionContext(primary, loopAssets: prepared));
            await RecordAiActionsBestEffortAsync(prepared.Select(item => item.Id), workflow, result);
            await app.StoreWorkflowTraceBestEffortAsync(workflow, result, assetId: primary.Id);
            var failed = result.Steps.Count(step => step.Status == WorkflowStepStatus.Failed);
            WorkflowStatusText.Text = result.Succeeded
                ? $"Image-loop execution complete · {prepared.Count} image(s) available to ForEachImage."
                : $"Image-loop workflow stopped · {failed} failed step(s).";
            await RefreshWorkflowTracesAsync();
        }
        catch (Exception ex)
        {
            if (executionStarted)
                await ((App)Application.Current).StoreWorkflowFailureTraceBestEffortAsync(workflow, dryRun: false, assetId: traceAssetId);
            Services.Log.Error("WorkflowHistoryLoop", ex);
            WorkflowStatusText.Text = ex.Message;
        }
    }

    private async void WorkflowDryRun_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowStatusText.Text = "Select a workflow first.";
            return;
        }
        var selection = SelectedHistoryDisplays(1).FirstOrDefault();
        if (selection is null)
        {
            WorkflowStatusText.Text = "Select exactly one History capture to dry-run this workflow.";
            return;
        }

        var executionStarted = false;
        try
        {
            var asset = await LoadHistoryAssetAsync(selection)
                ?? throw new InvalidOperationException("The selected History capture could not be loaded.");
            var app = (App)Application.Current;
            executionStarted = true;
            var result = await Services.WorkflowExecutor.ExecuteAsync(
                workflow,
                app.CreateWorkflowExecutionContext(asset, dryRun: true));
            await app.StoreWorkflowTraceBestEffortAsync(workflow, result, assetId: asset.Id);
            var wouldRun = result.Steps.Count(step => step.Status == WorkflowStepStatus.WouldRun);
            var failed = result.Steps.Count(step => step.Status == WorkflowStepStatus.Failed);
            WorkflowStatusText.Text = $"Dry-run complete · {result.Steps.Count} step(s) inspected · {wouldRun} side-effect/interactive step(s) suppressed"
                + (failed > 0 ? $" · {failed} failed" : string.Empty) + ".";
            await RefreshWorkflowTracesAsync();
        }
        catch (Exception ex)
        {
            if (executionStarted)
                await ((App)Application.Current).StoreWorkflowFailureTraceBestEffortAsync(workflow, dryRun: true, assetId: selection.Item.Id);
            Services.Log.Error("WorkflowDryRun", ex);
            WorkflowStatusText.Text = $"Dry-run stopped safely: {ex.Message}";
        }
    }

    private void ResetWorkflowBuilder()
    {
        _editingWorkflowId = null;
        WorkflowBuilderNameBox.Text = string.Empty;
        WorkflowBuilderDescriptionBox.Text = string.Empty;
        WorkflowBuilderVariablesBox.Text = string.Empty;
        WorkflowBuilderParameterList.Items.Clear();
        WorkflowBuilderStepList.Items.Clear();
        ResetWorkflowParameterEditor();
        ResetWorkflowStepEditor();
        WorkflowBuilderStatusText.Text = "New workflow. Add at least one step, then save locally.";
    }

    private void ResetWorkflowParameterEditor()
    {
        WorkflowBuilderParameterList.SelectedItem = null;
        WorkflowBuilderParameterNameBox.Text = string.Empty;
        WorkflowBuilderParameterPromptBox.Text = string.Empty;
        WorkflowBuilderParameterKindCombo.SelectedIndex = Math.Max(0, WorkflowBuilderParameterKindCombo.SelectedIndex);
        WorkflowBuilderParameterRequiredCheck.IsChecked = false;
        WorkflowBuilderParameterDefaultBox.Text = string.Empty;
        WorkflowBuilderParameterChoicesBox.Text = string.Empty;
    }

    private void ResetWorkflowStepEditor()
    {
        WorkflowBuilderStepList.SelectedItem = null;
        WorkflowBuilderAddKindCombo.SelectedIndex = Math.Max(0, WorkflowBuilderAddKindCombo.SelectedIndex);
        WorkflowBuilderStepEnabledCheck.IsChecked = true;
        WorkflowBuilderStepRequiredCheck.IsChecked = true;
        WorkflowBuilderStepArgumentBox.Text = string.Empty;
        WorkflowBuilderStepOutputKeyBox.Text = string.Empty;
        WorkflowBuilderStepConditionBox.Text = string.Empty;
        WorkflowBuilderStepOptionsBox.Text = string.Empty;
        WorkflowBuilderStepAttemptsBox.Value = 1;
        WorkflowBuilderStepRetryBox.Value = 0;
        WorkflowBuilderStepTimeoutBox.Value = 0;
    }

    private void WorkflowBuilderNew_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        ResetWorkflowBuilder();
    }

    private void WorkflowBuilderEditSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowBuilderStatusText.Text = "Select a workflow first.";
            return;
        }
        LoadWorkflowIntoBuilder(workflow, duplicate: workflow.IsBuiltIn);
    }

    private void WorkflowBuilderDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowBuilderStatusText.Text = "Select a workflow to duplicate first.";
            return;
        }
        LoadWorkflowIntoBuilder(workflow, duplicate: true);
    }

    private void LoadWorkflowIntoBuilder(CaptureWorkflow workflow, bool duplicate)
    {
        _editingWorkflowId = duplicate ? null : workflow.Id;
        WorkflowBuilderNameBox.Text = duplicate ? workflow.Name + " copy" : workflow.Name;
        WorkflowBuilderDescriptionBox.Text = workflow.Description;
        WorkflowBuilderVariablesBox.Text = workflow.Variables is null
            ? string.Empty
            : string.Join(Environment.NewLine, workflow.Variables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}"));
        WorkflowBuilderParameterList.Items.Clear();
        foreach (var parameter in workflow.Parameters ?? [])
            WorkflowBuilderParameterList.Items.Add(new WorkflowBuilderParameterView(parameter));
        WorkflowBuilderStepList.Items.Clear();
        foreach (var step in workflow.Steps)
            WorkflowBuilderStepList.Items.Add(new WorkflowBuilderStepView(step));
        ResetWorkflowParameterEditor();
        ResetWorkflowStepEditor();
        WorkflowBuilderStatusText.Text = duplicate
            ? $"Duplicated '{workflow.Name}' into an unsaved editable workflow."
            : $"Editing '{workflow.Name}' · schema {workflow.SchemaVersion}.";
    }

    private void WorkflowBuilderParameterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkflowBuilderParameterList.SelectedItem is not WorkflowBuilderParameterView item) return;
        var parameter = item.Definition;
        WorkflowBuilderParameterNameBox.Text = parameter.Name;
        WorkflowBuilderParameterPromptBox.Text = parameter.Prompt;
        WorkflowBuilderParameterKindCombo.SelectedItem = parameter.Kind;
        WorkflowBuilderParameterRequiredCheck.IsChecked = parameter.Required;
        WorkflowBuilderParameterDefaultBox.Text = parameter.DefaultValue ?? string.Empty;
        WorkflowBuilderParameterChoicesBox.Text = parameter.Choices is null
            ? string.Empty
            : string.Join(Environment.NewLine, parameter.Choices);
    }

    private void WorkflowBuilderAddParameter_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        try
        {
            if (WorkflowBuilderParameterList.Items.Count >= WorkflowRuntimePolicy.MaximumParameters)
                throw new InvalidOperationException($"A workflow can contain at most {WorkflowRuntimePolicy.MaximumParameters} parameters.");
            var definition = BuildWorkflowParameterFromEditor();
            var candidate = WorkflowBuilderParameterList.Items.OfType<WorkflowBuilderParameterView>()
                .Select(item => item.Definition).Append(definition).ToArray();
            ValidateWorkflowParameterSet(candidate);
            var view = new WorkflowBuilderParameterView(definition);
            WorkflowBuilderParameterList.Items.Add(view);
            WorkflowBuilderParameterList.SelectedItem = view;
            WorkflowBuilderStatusText.Text = $"Added parameter '{definition.Name}'.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowBuilderParameterAdd", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private void WorkflowBuilderApplyParameter_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || WorkflowBuilderParameterList.SelectedItem is not WorkflowBuilderParameterView selected) return;
        try
        {
            var index = WorkflowBuilderParameterList.Items.IndexOf(selected);
            if (index < 0) return;
            var definition = BuildWorkflowParameterFromEditor();
            var candidate = WorkflowBuilderParameterList.Items.OfType<WorkflowBuilderParameterView>()
                .Select(item => item == selected ? definition : item.Definition).ToArray();
            ValidateWorkflowParameterSet(candidate);
            var replacement = new WorkflowBuilderParameterView(definition);
            WorkflowBuilderParameterList.Items.RemoveAt(index);
            WorkflowBuilderParameterList.Items.Insert(index, replacement);
            WorkflowBuilderParameterList.SelectedItem = replacement;
            WorkflowBuilderStatusText.Text = $"Updated parameter '{definition.Name}'.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowBuilderParameterApply", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private void WorkflowBuilderRemoveParameter_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || WorkflowBuilderParameterList.SelectedItem is not WorkflowBuilderParameterView item) return;
        WorkflowBuilderParameterList.Items.Remove(item);
        ResetWorkflowParameterEditor();
        WorkflowBuilderStatusText.Text = $"Removed parameter '{item.Definition.Name}'.";
    }

    private WorkflowParameterDefinition BuildWorkflowParameterFromEditor()
    {
        var kind = WorkflowBuilderParameterKindCombo.SelectedItem is WorkflowParameterKind selected
            ? selected
            : WorkflowParameterKind.Text;
        var choices = WorkflowBuilderParameterChoicesBox.Text
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        return new WorkflowParameterDefinition(
            WorkflowBuilderParameterNameBox.Text.Trim(),
            WorkflowBuilderParameterPromptBox.Text.Trim(),
            kind,
            WorkflowBuilderParameterRequiredCheck.IsChecked == true,
            NullIfWhiteSpace(WorkflowBuilderParameterDefaultBox.Text),
            kind == WorkflowParameterKind.Choice ? choices : null);
    }

    private static void ValidateWorkflowParameterSet(IReadOnlyList<WorkflowParameterDefinition> parameters)
    {
        var preview = new CaptureWorkflow(
            "preview", "Preview", string.Empty, ProductTier.PlusTrial,
            [new WorkflowStep("preview-copy", WorkflowStepKind.CopyImage)],
            SchemaVersion: 5,
            Parameters: parameters);
        var validation = WorkflowValidator.Validate(preview);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
    }

    private void WorkflowBuilderStepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkflowBuilderStepList.SelectedItem is not WorkflowBuilderStepView item) return;
        var step = item.Step;
        WorkflowBuilderAddKindCombo.SelectedItem = step.Kind;
        WorkflowBuilderStepEnabledCheck.IsChecked = step.IsEnabled != false;
        WorkflowBuilderStepRequiredCheck.IsChecked = step.Required;
        WorkflowBuilderStepArgumentBox.Text = step.Argument ?? string.Empty;
        WorkflowBuilderStepOutputKeyBox.Text = step.OutputKey ?? string.Empty;
        WorkflowBuilderStepConditionBox.Text = step.Condition ?? string.Empty;
        WorkflowBuilderStepOptionsBox.Text = step.Options is null
            ? string.Empty
            : string.Join(Environment.NewLine, step.Options.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}"));
        WorkflowBuilderStepAttemptsBox.Value = step.MaxAttempts;
        WorkflowBuilderStepRetryBox.Value = step.RetryDelayMilliseconds;
        WorkflowBuilderStepTimeoutBox.Value = step.TimeoutMilliseconds;
    }

    private void WorkflowBuilderAddStep_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        var kind = WorkflowBuilderAddKindCombo.SelectedItem is WorkflowStepKind selected ? selected : WorkflowStepKind.CopyImage;
        var id = $"step-{WorkflowBuilderStepList.Items.Count + 1}-{Guid.NewGuid():N}";
        id = id[..Math.Min(id.Length, 32)];
        var step = new WorkflowStep(id, kind);
        var item = new WorkflowBuilderStepView(step);
        WorkflowBuilderStepList.Items.Add(item);
        WorkflowBuilderStepList.SelectedItem = item;
        WorkflowBuilderStatusText.Text = $"Added {kind}. Configure it below, then apply step settings.";
    }

    private void WorkflowBuilderRemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || WorkflowBuilderStepList.SelectedItem is not WorkflowBuilderStepView item) return;
        WorkflowBuilderStepList.Items.Remove(item);
        ResetWorkflowStepEditor();
        WorkflowBuilderStatusText.Text = "Removed the selected step.";
    }

    private void WorkflowBuilderMoveUp_Click(object sender, RoutedEventArgs e) => MoveWorkflowBuilderStep(-1);
    private void WorkflowBuilderMoveDown_Click(object sender, RoutedEventArgs e) => MoveWorkflowBuilderStep(1);

    private void MoveWorkflowBuilderStep(int delta)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || WorkflowBuilderStepList.SelectedItem is not WorkflowBuilderStepView item) return;
        var index = WorkflowBuilderStepList.Items.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= WorkflowBuilderStepList.Items.Count) return;
        WorkflowBuilderStepList.Items.RemoveAt(index);
        WorkflowBuilderStepList.Items.Insert(target, item);
        WorkflowBuilderStepList.SelectedItem = item;
        WorkflowBuilderStatusText.Text = "Step order updated. Drag-and-drop can be used for larger rearrangements.";
    }

    private void WorkflowBuilderApplyStep_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || WorkflowBuilderStepList.SelectedItem is not WorkflowBuilderStepView selected) return;
        try
        {
            var index = WorkflowBuilderStepList.Items.IndexOf(selected);
            if (index < 0) return;
            var kind = WorkflowBuilderAddKindCombo.SelectedItem is WorkflowStepKind selectedKind ? selectedKind : selected.Step.Kind;
            var options = ParseEditorPairs(WorkflowBuilderStepOptionsBox.Text, "Step options", validateWorkflowVariables: false);
            var updated = selected.Step with
            {
                Kind = kind,
                Required = WorkflowBuilderStepRequiredCheck.IsChecked == true,
                Argument = NullIfWhiteSpace(WorkflowBuilderStepArgumentBox.Text),
                OutputKey = NullIfWhiteSpace(WorkflowBuilderStepOutputKeyBox.Text),
                Options = options,
                Condition = NullIfWhiteSpace(WorkflowBuilderStepConditionBox.Text),
                MaxAttempts = SafeNumber(WorkflowBuilderStepAttemptsBox.Value, 1, 1, 5),
                RetryDelayMilliseconds = SafeNumber(WorkflowBuilderStepRetryBox.Value, 0, 0, 60_000),
                TimeoutMilliseconds = SafeNumber(WorkflowBuilderStepTimeoutBox.Value, 0, 0, 600_000),
                IsEnabled = WorkflowBuilderStepEnabledCheck.IsChecked == true
            };
            var candidate = new CaptureWorkflow("preview", "Preview", string.Empty, ProductTier.PlusTrial, [updated], SchemaVersion: 4);
            var validation = WorkflowValidator.Validate(candidate);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            var replacement = new WorkflowBuilderStepView(updated);
            WorkflowBuilderStepList.Items.RemoveAt(index);
            WorkflowBuilderStepList.Items.Insert(index, replacement);
            WorkflowBuilderStepList.SelectedItem = replacement;
            WorkflowBuilderStatusText.Text = $"Applied settings to {updated.Kind}.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowBuilderStep", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private async void WorkflowBuilderSave_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        try
        {
            var all = await Services.Workflows.LoadAsync();
            var workflow = BuildWorkflowFromStudio();
            var custom = all.Where(item => !item.IsBuiltIn).ToList();
            var index = custom.FindIndex(item => string.Equals(item.Id, workflow.Id, StringComparison.Ordinal));
            if (index >= 0) custom[index] = workflow;
            else custom.Add(workflow);
            await Services.Workflows.SaveCustomAsync(custom);
            _editingWorkflowId = workflow.Id;
            await RefreshWorkflowsAsync();
            WorkflowList.SelectedItem = _workflowItems.FirstOrDefault(item => string.Equals(item.Id, workflow.Id, StringComparison.Ordinal));
            WorkflowBuilderStatusText.Text = $"Saved '{workflow.Name}' with {workflow.Steps.Count} step(s).";
        }
        catch (Exception ex)
        {
                Services.Log.Error("WorkflowBuilderSave", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private async void WorkflowBuilderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (string.IsNullOrWhiteSpace(_editingWorkflowId))
        {
            WorkflowBuilderStatusText.Text = "This editor is not attached to a saved custom workflow.";
            return;
        }
        try
        {
            var all = await Services.Workflows.LoadAsync();
            var triggers = await Services.WorkflowTriggers.LoadAsync();
            var dependents = WorkflowReferencePolicy.FindWorkflowDependents(_editingWorkflowId, all, triggers);
            if (dependents.Count > 0)
                throw new InvalidOperationException("Workflow is still referenced by " + string.Join(", ", dependents) + ". Remove those references first.");
            var custom = all.Where(item => !item.IsBuiltIn && !string.Equals(item.Id, _editingWorkflowId, StringComparison.Ordinal)).ToArray();
            if (custom.Length == all.Count(item => !item.IsBuiltIn))
                throw new InvalidOperationException("The custom workflow no longer exists.");
            var deletedId = _editingWorkflowId;
            await Services.Workflows.SaveCustomAsync(custom);
            var cleaned = await ((App)Application.Current).TryMutateSettingsAsync(
                current => SettingsReferencePolicy.RemoveWorkflowReferences(current, deletedId),
                SettingsRuntimeEffects.MainWindowUi,
                logComponent: "WorkflowDeleteSettingsCleanup");
            ResetWorkflowBuilder();
            await RefreshWorkflowsAsync();
            WorkflowBuilderStatusText.Text = cleaned
                ? "Deleted the custom workflow."
                : "Workflow was deleted, but related settings could not be persisted. Restart before editing capture profiles or hotkeys.";
        }
        catch (Exception ex)
        {
                Services.Log.Error("WorkflowBuilderDelete", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private async void WorkflowBuilderImport_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".magicworkflow");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var workflow = await Services.Workflows.ImportAsync(file.Path);
            LoadWorkflowIntoBuilder(workflow, duplicate: true);
            WorkflowBuilderStatusText.Text = $"Imported '{workflow.Name}' into the editor. Review it, then Save to add it locally.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowBuilderImport", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private async void WorkflowBuilderExport_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (WorkflowList.SelectedItem is not CaptureWorkflow workflow)
        {
            WorkflowBuilderStatusText.Text = "Select a workflow to export first.";
            return;
        }
        var picker = new FileSavePicker { SuggestedFileName = SanitizeWorkflowFileName(workflow.Name) };
        picker.FileTypeChoices.Add("Magic Capture workflow", new List<string> { ".magicworkflow" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await Services.Workflows.ExportAsync(workflow, file.Path);
            WorkflowBuilderStatusText.Text = $"Exported '{workflow.Name}'.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowBuilderExport", ex);
            WorkflowBuilderStatusText.Text = ex.Message;
        }
    }

    private CaptureWorkflow BuildWorkflowFromStudio()
    {
        var name = WorkflowBuilderNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Workflow name is required.");
        var variables = ParseEditorPairs(WorkflowBuilderVariablesBox.Text, "Workflow variables", validateWorkflowVariables: true);
        var parameters = WorkflowBuilderParameterList.Items.OfType<WorkflowBuilderParameterView>().Select(item => item.Definition).ToArray();
        var steps = WorkflowBuilderStepList.Items.OfType<WorkflowBuilderStepView>().Select(item => item.Step).ToArray();
        if (steps.Length == 0) throw new InvalidOperationException("Add at least one workflow step.");
        var id = _editingWorkflowId ?? CreateWorkflowId(name);
        var tier = InferWorkflowTier(steps);
        var workflow = new CaptureWorkflow(
            id,
            name,
            WorkflowBuilderDescriptionBox.Text.Trim(),
            tier,
            steps,
            SchemaVersion: 5,
            Variables: variables,
            Parameters: parameters.Length == 0 ? null : parameters);
        var validation = WorkflowValidator.Validate(workflow);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        return workflow;
    }

    private async void ResumeWorkflowTrace_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        if (WorkflowTraceList.SelectedItem is not WorkflowTraceView view)
        {
            WorkflowStatusText.Text = "Select a failed workflow trace first.";
            return;
        }

        var trace = view.Record;
        CaptureWorkflow? workflow = null;
        Guid? assetId = trace.AssetId;
        var executionStarted = false;
        IReadOnlySet<string>? resumeCheckpoint = null;
        try
        {
            workflow = (await Services.Workflows.LoadAsync()).FirstOrDefault(candidate => string.Equals(candidate.Id, trace.WorkflowId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The workflow referenced by this trace no longer exists.");
            var plan = WorkflowResumePlanner.CreatePlan(workflow, trace);
            resumeCheckpoint = plan.CompletedSafeSideEffectStepIds;
            if (!plan.IsEligible || plan.AssetId is null)
            {
                WorkflowStatusText.Text = $"Resume unavailable: {plan.Reason}";
                return;
            }

            var asset = await LoadHistoryAssetByIdAsync(plan.AssetId.Value)
                ?? throw new InvalidOperationException("The source History capture is missing or could not be loaded.");
            await RecordHistoryWorkflowStartBestEffortAsync([asset.Id], workflow);
            var app = (App)Application.Current;
            var workflowAsset = await app.PrepareWorkflowAssetAsync(asset, workflow);
            var context = app.CreateWorkflowExecutionContext(workflowAsset) with
            {
                IsResume = true,
                ResumeCompletedSideEffectStepIds = resumeCheckpoint
            };
            executionStarted = true;
            var result = await Services.WorkflowExecutor.ExecuteAsync(workflow, context);
            await RecordAiActionsBestEffortAsync([asset.Id], workflow, result);
            await app.StoreWorkflowTraceBestEffortAsync(workflow, result, assetId: asset.Id, resumedFromTraceId: trace.TraceId, resumeCompletedSideEffectStepIds: resumeCheckpoint);
            WorkflowStatusText.Text = result.Succeeded
                ? "Resume replay completed successfully. Previously completed safe side effects were not repeated."
                : "Resume replay stopped again; a new trace was recorded.";
            await RefreshWorkflowTracesAsync();
        }
        catch (Exception ex)
        {
            if (executionStarted && workflow is not null)
                await ((App)Application.Current).StoreWorkflowFailureTraceBestEffortAsync(
                    workflow, dryRun: false, assetId: assetId, resumedFromTraceId: trace.TraceId,
                    resumeCompletedSideEffectStepIds: resumeCheckpoint);
            Services.Log.Error("WorkflowResume", ex);
            WorkflowStatusText.Text = $"Resume stopped safely: {ex.Message}";
        }
    }

    private async Task RefreshWorkflowTracesAsync()
    {
        if (_services is null) return;
        try
        {
            var traces = await Services.WorkflowTraces.LoadAsync();
            var views = traces.Select(trace => new WorkflowTraceView(trace)).ToArray();
            WorkflowTraceList.ItemsSource = views;
            if (views.Length == 0)
            {
                WorkflowTraceDetailsBox.Text = "No local workflow traces yet.";
                return;
            }
            if (WorkflowTraceList.SelectedItem is null) WorkflowTraceList.SelectedItem = views[0];
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowTraceLoad", ex);
            WorkflowTraceList.ItemsSource = Array.Empty<WorkflowTraceView>();
            WorkflowTraceDetailsBox.Text = "Workflow trace metadata could not be loaded safely. The trace file was left unchanged.";
        }
    }

    private async void RefreshWorkflowTraces_Click(object sender, RoutedEventArgs e) => await RefreshWorkflowTracesAsync();

    private async void ClearWorkflowTraces_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Services.WorkflowTraces.ClearAsync();
            WorkflowTraceList.ItemsSource = Array.Empty<WorkflowTraceView>();
            WorkflowTraceDetailsBox.Text = "Local workflow traces cleared.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("WorkflowTraceClear", ex);
            WorkflowTraceDetailsBox.Text = "Workflow traces could not be cleared. No trace file was treated as successfully deleted.";
        }
    }

    private void WorkflowTraceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkflowTraceList.SelectedItem is not WorkflowTraceView view)
        {
            WorkflowTraceDetailsBox.Text = string.Empty;
            return;
        }
        var trace = view.Record;
        var lines = new List<string>
        {
            $"Trace: {trace.TraceId}",
            $"Workflow: {trace.WorkflowName} ({trace.WorkflowId})",
            $"Schema: {trace.SchemaVersion}",
            $"Mode: {(trace.DryRun ? "Dry-run" : "Execution")}",
            $"Result: {(trace.Succeeded ? "Succeeded" : "Failed")}",
            $"Started: {trace.StartedUtc.LocalDateTime:G}",
            $"Finished: {trace.FinishedUtc.LocalDateTime:G}",
            $"Duration: {Math.Max(0, (long)(trace.FinishedUtc - trace.StartedUtc).TotalMilliseconds)} ms",
            $"Source capture: {trace.AssetId?.ToString() ?? "not recorded"}",
            $"Workflow fingerprint: {(string.IsNullOrWhiteSpace(trace.WorkflowFingerprint) ? "not recorded" : trace.WorkflowFingerprint)}",
            $"Resumed from: {trace.ResumedFromTraceId?.ToString() ?? "no"}",
            string.Empty,
            "Steps"
        };
        foreach (var step in trace.Steps)
        {
            lines.Add($"- {step.Kind} · {step.Status} · attempts {step.Attempts} · {step.DurationMilliseconds} ms · {step.StepId}");
            if (!string.IsNullOrWhiteSpace(step.Message)) lines.Add($"  {step.Message}");
        }
        WorkflowTraceDetailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private static ProductTier InferWorkflowTier(IReadOnlyList<WorkflowStep> steps) =>
        steps.Any(step => step.IsEnabled != false && step.Kind is WorkflowStepKind.RunMagicAction or WorkflowStepKind.CustomHttpDestination)
            ? ProductTier.ProLifetime
            : ProductTier.PlusTrial;

    private static string CreateWorkflowId(string name)
    {
        var slug = string.Concat(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "workflow";
        if (slug.Length > 52) slug = slug[..52];
        var candidate = $"custom-{slug}-{Guid.NewGuid():N}";
        return candidate.Length <= 96 ? candidate : candidate[..96];
    }

    private static string SanitizeWorkflowFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(value) ? "workflow" : value;
    }

    private static IReadOnlyDictionary<string, string>? ParseEditorPairs(string text, string label, bool validateWorkflowVariables)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var separator = raw.IndexOf('=');
            if (separator <= 0) throw new InvalidOperationException($"{label}: expected name=value, got '{raw}'.");
            var key = raw[..separator].Trim();
            var value = raw[(separator + 1)..];
            if (!result.TryAdd(key, value)) throw new InvalidOperationException($"{label}: duplicate key '{key}'.");
        }
        if (validateWorkflowVariables)
        {
            var errors = WorkflowVariables.Validate(result, label);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        }
        return result.Count == 0 ? null : result;
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task RefreshLocalActionsAsync()
    {
        if (_services is null) return;
        var selectedId = (LocalActionList.SelectedItem as LocalActionProfile)?.Id ?? _editingLocalActionId;
        _localActionsLoadHealthy = false;
        try
        {
            _localActionItems = await Services.LocalActions.LoadAsync();
            _localActionsLoadHealthy = true;
            LocalActionList.ItemsSource = _localActionItems;
            var selected = _localActionItems.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal));
            LocalActionList.SelectedItem = selected ?? _localActionItems.FirstOrDefault();
            if (_localActionItems.Count == 0 && _editingLocalActionId is null) ResetLocalActionEditor();
            LocalActionStatusText.Text = _localActionItems.Count == 0
                ? "No Local Actions yet. Add a direct executable profile to use it from workflows."
                : $"{_localActionItems.Count} Local Action profile(s) loaded.";
        }
        catch (Exception ex)
        {
            LocalActionList.ItemsSource = Array.Empty<LocalActionProfile>();
            LocalActionStatusText.Text = "Local Actions could not be loaded safely. The file was not treated as empty; repair or restore the local JSON before saving.";
            Services.Log.Error("LocalActionLoad", ex);
        }
    }

    private void LocalActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalActionList.SelectedItem is not LocalActionProfile profile) return;
        _editingLocalActionId = profile.Id;
        LocalActionNameBox.Text = profile.Name;
        LocalActionExecutableBox.Text = profile.ExecutablePath;
        LocalActionWorkingDirectoryBox.Text = profile.WorkingDirectory ?? string.Empty;
        LocalActionArgumentsBox.Text = string.Join(Environment.NewLine, profile.Arguments);
        SelectTag(LocalActionOutputCombo, profile.OutputMode.ToString());
        LocalActionOutputExtensionBox.Text = profile.OutputFileExtension;
        LocalActionTimeoutBox.Value = profile.TimeoutMilliseconds;
        LocalActionStdoutLimitBox.Value = profile.MaxStdoutBytes / 1024d;
        LocalActionStderrLimitBox.Value = profile.MaxStderrBytes / 1024d;
        LocalActionFileLimitBox.Value = profile.MaxOutputFileBytes / (1024d * 1024d);
        LocalActionEnabledCheck.IsChecked = profile.Enabled;
    }

    private void LocalActionNew_Click(object sender, RoutedEventArgs e)
    {
        LocalActionList.SelectedItem = null;
        _editingLocalActionId = null;
        ResetLocalActionEditor();
        LocalActionStatusText.Text = "New Local Action. Only .exe/.com launch targets are accepted; scripts must go through an explicitly approved interpreter executable.";
    }

    private void ResetLocalActionEditor()
    {
        LocalActionNameBox.Text = string.Empty;
        LocalActionExecutableBox.Text = string.Empty;
        LocalActionWorkingDirectoryBox.Text = string.Empty;
        LocalActionArgumentsBox.Text = "$input";
        SelectTag(LocalActionOutputCombo, LocalActionOutputMode.StdoutText.ToString());
        LocalActionOutputExtensionBox.Text = ".out";
        LocalActionTimeoutBox.Value = 30_000;
        LocalActionStdoutLimitBox.Value = 1024;
        LocalActionStderrLimitBox.Value = 256;
        LocalActionFileLimitBox.Value = 16;
        LocalActionEnabledCheck.IsChecked = true;
    }

    private LocalActionProfile BuildLocalActionFromEditor()
    {
        var outputTag = (LocalActionOutputCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (!Enum.TryParse<LocalActionOutputMode>(outputTag, out var outputMode)) outputMode = LocalActionOutputMode.StdoutText;
        var arguments = LocalActionArgumentsBox.Text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n').Where(line => line.Length > 0).Take(LocalActionProfileValidator.MaximumArguments + 1).ToArray();
        var executable = Environment.ExpandEnvironmentVariables(LocalActionExecutableBox.Text.Trim());
        var working = string.IsNullOrWhiteSpace(LocalActionWorkingDirectoryBox.Text)
            ? null
            : Environment.ExpandEnvironmentVariables(LocalActionWorkingDirectoryBox.Text.Trim());
        var extension = string.IsNullOrWhiteSpace(LocalActionOutputExtensionBox.Text) ? ".out" : LocalActionOutputExtensionBox.Text.Trim();
        if (outputMode == LocalActionOutputMode.OutputFileImage && !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image chaining currently requires a .png $output extension.");

        var timeoutMs = SafeNumber(LocalActionTimeoutBox.Value, 30_000, 100, LocalActionProfileValidator.MaximumTimeoutMilliseconds);
        var stdoutKiB = SafeNumber(LocalActionStdoutLimitBox.Value, 1024, 0, LocalActionProfileValidator.MaximumCapturedStreamBytes / 1024);
        var stderrKiB = SafeNumber(LocalActionStderrLimitBox.Value, 256, 0, LocalActionProfileValidator.MaximumCapturedStreamBytes / 1024);
        var fileMiB = SafeNumber(LocalActionFileLimitBox.Value, 16, 0, LocalActionProfileValidator.MaximumOutputFileBytes / (1024 * 1024));
        return new LocalActionProfile(
            _editingLocalActionId ?? Guid.NewGuid().ToString("N"),
            LocalActionNameBox.Text.Trim(),
            executable,
            arguments,
            outputMode,
            extension,
            working,
            timeoutMs,
            checked(stdoutKiB * 1024),
            checked(stderrKiB * 1024),
            checked(fileMiB * 1024 * 1024),
            LocalActionEnabledCheck.IsChecked == true);
    }

    private static int SafeNumber(double value, int fallback, int minimum, int maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        return Math.Clamp((int)Math.Round(value), minimum, maximum);
    }

    private async void LocalActionSave_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        try
        {
            if (!_localActionsLoadHealthy) await RefreshLocalActionsAsync();
            if (!_localActionsLoadHealthy) return;
            var profile = BuildLocalActionFromEditor();
            var validation = LocalActionProfileValidator.Validate(profile);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));

            var profiles = _localActionItems.ToList();
            var index = profiles.FindIndex(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal));
            if (index >= 0) profiles[index] = profile;
            else profiles.Add(profile);
            await Services.LocalActions.SaveAsync(profiles);
            _editingLocalActionId = profile.Id;
            await RefreshLocalActionsAsync();
            LocalActionList.SelectedItem = _localActionItems.FirstOrDefault(item => item.Id == profile.Id);
            LocalActionStatusText.Text = $"Saved Local Action '{profile.Name}'. Executable approval is requested only when it is first run or its SHA-256 changes.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("LocalActionSave", ex);
            LocalActionStatusText.Text = ex.Message;
        }
    }

    private async void LocalActionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || LocalActionList.SelectedItem is not LocalActionProfile selected) return;
        try
        {
            if (!_localActionsLoadHealthy) await RefreshLocalActionsAsync();
            if (!_localActionsLoadHealthy) return;
            var workflows = await Services.Workflows.LoadAsync();
            var dependents = WorkflowReferencePolicy.FindLocalActionDependents(selected.Id, workflows, _magicRecipes);
            if (dependents.Count > 0)
                throw new InvalidOperationException("Local Action is still referenced by " + string.Join(", ", dependents) + ". Remove those references first.");
            var profiles = _localActionItems.Where(item => !string.Equals(item.Id, selected.Id, StringComparison.Ordinal)).ToArray();
            await Services.LocalActions.SaveAsync(profiles);
            _editingLocalActionId = null;
            await RefreshLocalActionsAsync();
            LocalActionStatusText.Text = $"Deleted Local Action '{selected.Name}'. Its executable approval was left intact; revoke it separately if desired.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("LocalActionDelete", ex);
            LocalActionStatusText.Text = ex.Message;
        }
    }

    private async void LocalActionRevokeApproval_Click(object sender, RoutedEventArgs e)
    {
        if (LocalActionList.SelectedItem is not LocalActionProfile selected) return;
        try
        {
            await Services.LocalActionApprovals.RevokeAsync(Path.GetFullPath(selected.ExecutablePath));
            LocalActionStatusText.Text = $"Revoked approval for {selected.ExecutablePath}. The next run must be approved again.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("LocalActionRevoke", ex);
            LocalActionStatusText.Text = ex.Message;
        }
    }

    private async void LocalActionTest_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || LocalActionList.SelectedItem is not LocalActionProfile selected) return;
        var history = SelectedHistoryDisplays(maximumCount: 1).FirstOrDefault();
        if (history is null)
        {
            LocalActionStatusText.Text = "Select one capture in History first.";
            return;
        }

        try
        {
            var asset = await LoadHistoryAssetAsync(history) ?? throw new InvalidOperationException("The selected History capture could not be loaded.");
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["workflow"] = "Local Action test",
                ["filename"] = "capture.png",
                ["width"] = asset.Width,
                ["height"] = asset.Height,
                ["size"] = asset.PngBytes.LongLength,
                ["source"] = asset.SourceDisplayName ?? asset.SourceKind.ToString()
            };
            if (selected.Arguments.Any(argument => LocalActionTemplate.References(argument, "ocrText")))
            {
                var ocr = await Services.Ocr.RecognizeAsync(asset.PngBytes, Services.Settings.PreferredOcrLanguage);
                values["ocr"] = ocr;
                values["text"] = ocr.Text;
            }
            var app = (App)Application.Current;
            var result = await Services.LocalActionRunner.ExecuteAsync(
                selected,
                new LocalActionExecutionContext(asset, values, app.ConfirmLocalActionApprovalAsync));
            var stdout = TruncateForStatus(result.Stdout, 1200);
            var stderr = TruncateForStatus(result.Stderr, 800);
            LocalActionStatusText.Text = $"Exit {result.ExitCode} · {result.Duration.TotalMilliseconds:N0} ms · output {(result.OutputBytes?.Length ?? 0):N0} bytes"
                + (string.IsNullOrWhiteSpace(stdout) ? string.Empty : $"\nstdout: {stdout}")
                + (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $"\nstderr: {stderr}");
        }
        catch (Exception ex)
        {
            Services.Log.Error("LocalActionTest", ex);
            LocalActionStatusText.Text = ex.Message;
        }
    }

    private async void LocalActionCreateWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows) || LocalActionList.SelectedItem is not LocalActionProfile selected) return;
        try
        {
            var all = await Services.Workflows.LoadAsync();
            var custom = all.Where(workflow => !workflow.IsBuiltIn).ToList();
            var baseId = "local-action-" + selected.Id;
            var id = baseId.Length <= 96 ? baseId : baseId[..96];
            var workflow = new CaptureWorkflow(
                id,
                selected.Name,
                $"Run the approved Local Action '{selected.Name}' and chain its configured output.",
                ProductTier.PlusTrial,
                [new WorkflowStep("run-local-action", WorkflowStepKind.RunLocalAction, Argument: selected.Id)],
                SchemaVersion: 3);
            var index = custom.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (index >= 0) custom[index] = workflow;
            else custom.Add(workflow);
            await Services.Workflows.SaveCustomAsync(custom);
            await RefreshWorkflowsAsync();
            WorkflowList.SelectedItem = _workflowItems.FirstOrDefault(item => item.Id == id);
            LocalActionStatusText.Text = $"Workflow '{workflow.Name}' is ready for History, Capture Profiles and CLI.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("LocalActionCreateWorkflow", ex);
            LocalActionStatusText.Text = ex.Message;
        }
    }

    private static string TruncateForStatus(string? value, int maximum)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maximum ? value : value[..maximum] + "…";
    }

    private void WatchStart_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        try
        {
            var intervalSeconds = Math.Clamp((int)Math.Round(WatchIntervalBox.Value), 1, 3600);
            var changedOnly = WatchChangedOnlyCheck.IsChecked == true;
            var threshold = Math.Clamp(WatchThresholdBox.Value, 0, 100);
            var selected = WatchWorkflowCombo.SelectedItem as WatchWorkflowOption ?? new WatchWorkflowOption("History only", null, ProductTier.Free);
            var tier = Services.Entitlements.Current.Tier;
            if (tier == ProductTier.Free && intervalSeconds < 10)
                throw new InvalidOperationException("Free Capture Watch has a 10-second minimum interval. Plus/Pro can run from 1 second.");
            if (changedOnly && !Services.Entitlements.CanUse(ProductFeature.ChangeAwareCaptureWatch))
            {
                ShowPlan(ProductFeature.ChangeAwareCaptureWatch);
                return;
            }
            if ((int)tier < (int)selected.RequiredTier)
            {
                ShowPlan(ProductFeature.AdvancedWorkflows);
                return;
            }

            Services.CaptureWatch.Tick -= CaptureWatch_Tick;
            Services.CaptureWatch.Stopped -= CaptureWatch_Stopped;
            Services.CaptureWatch.Tick += CaptureWatch_Tick;
            Services.CaptureWatch.Stopped += CaptureWatch_Stopped;
            Services.CaptureWatch.Start(
                new CaptureWatchOptions(TimeSpan.FromSeconds(intervalSeconds), threshold, changedOnly, selected.WorkflowId),
                async (tick, cancellationToken) => await DispatchWatchCaptureAsync(tick, selected.WorkflowId, cancellationToken));
            WatchStartButton.IsEnabled = false;
            WatchStopButton.IsEnabled = true;
            WatchStatusText.Text = "Watching last region…";
        }
        catch (Exception ex) { WatchStatusText.Text = ex.Message; }
    }

    private void WatchStop_Click(object sender, RoutedEventArgs e) => Services.CaptureWatch.Stop();

    private void CaptureWatch_Tick(object? sender, CaptureWatchTick tick)
    {
        DispatcherQueue.TryEnqueue(() => WatchStatusText.Text = $"#{tick.Sequence} · change {tick.ChangedPercent:0.00}% · {(tick.Triggered ? "triggered" : "skipped")}");
    }

    private void CaptureWatch_Stopped(object? sender, string reason)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            WatchStartButton.IsEnabled = true;
            WatchStopButton.IsEnabled = false;
            WatchStatusText.Text = reason;
        });
    }

    private Task DispatchWatchCaptureAsync(CaptureWatchTick tick, string? workflowId, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Services.HistoryStore.AddAsync(tick.Asset, Services.Settings);
                if (!string.IsNullOrWhiteSpace(workflowId)) await ((App)Application.Current).RunWorkflowAsync(tick.Asset, workflowId);
                RefreshHistorySoon();
                completion.TrySetResult(true);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new InvalidOperationException("Could not dispatch Capture Watch result to the UI thread."));
        return completion.Task.WaitAsync(cancellationToken);
    }

    private async void UtilityInspect_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var report = await Task.Run(() => Services.Metadata.Inspect(asset.PngBytes));
        UtilityOutputBox.Text = $"Size: {report.Width} × {report.Height}\r\nDPI: {report.HorizontalDpi:0.##} × {report.VerticalDpi:0.##}\r\nPixel format: {report.PixelFormat}\r\nBytes: {report.ByteLength:N0}\r\nSHA-256: {report.Sha256}\r\nSHA-1: {report.Sha1}\r\nMD5: {report.Md5}\r\n" +
            string.Join("\r\n", report.Properties.Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    private async void UtilityBeautify_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var bytes = await Task.Run(() => Services.ImageUtilities.Beautify(asset.PngBytes, new BeautifyOptions()));
        ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, "Beautified"));
    }

    private async void UtilityStripMetadata_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var bytes = await Task.Run(() => Services.ImageUtilities.StripMetadata(asset.PngBytes));
        ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, "Metadata stripped"));
    }

    private async void UtilityThumbnail_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var bytes = await Task.Run(() => Services.ImageUtilities.Thumbnail(asset.PngBytes, 320, 180));
        ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, "Thumbnail"));
    }

    private async void UtilityCombineHorizontal_Click(object sender, RoutedEventArgs e) => await CombineSelectedAsync(ImageCombineMode.Horizontal);
    private async void UtilityCombineVertical_Click(object sender, RoutedEventArgs e) => await CombineSelectedAsync(ImageCombineMode.Vertical);

    private async Task CombineSelectedAsync(ImageCombineMode mode)
    {
        if (!EnsurePlus(ProductFeature.UtilityImagePack)) return;
        var assets = await TryLoadSelectedHistoryAssetsAsync(128, "UtilityCombineSelection");
        if (assets is null) return;
        if (assets.Count < 2) { UtilityOutputBox.Text = "Select at least two captures in History first."; return; }
        var bytes = await Task.Run(() => Services.ImageUtilities.Combine(assets.Select(a => a.PngBytes).ToArray(), mode, spacing: 8));
        ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, mode == ImageCombineMode.Horizontal ? "Combined horizontal" : "Combined vertical"));
    }

    private async void UtilitySplit_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.UtilityImagePack)) return;
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var parts = await Task.Run(() => Services.ImageUtilities.Split(asset.PngBytes, 2, 2));
        foreach (var (bytes, index) in parts.Select((bytes, index) => (bytes, index)))
            ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, $"Split {index + 1}"));
    }

    private async void UtilityPdf_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        try
        {
            var pdf = await Task.Run(() => Services.PdfExport.Create([asset.PngBytes]));
            var file = await Services.Export.SaveBytesAsAsync(this, pdf, "PDF document", ".pdf", "Magic Capture Desktop capture");
            if (file is not null) UtilityOutputBox.Text = $"PDF saved: {file.Path}\r\n{pdf.LongLength:N0} bytes";
        }
        catch (Exception ex) { Services.Log.Error("UtilityPdf", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityMultiPdf_Click(object sender, RoutedEventArgs e)
    {
        var displays = SelectedHistoryDisplays(PdfImageDocumentWriter.MaximumPages);
        if (displays.Count == 0) { UtilityOutputBox.Text = "Select one or more captures in History first."; return; }
        try
        {
            var paths = displays.Select(display => Services.HistoryStore.GetAbsolutePath(display.Item)).ToArray();
            var pdf = await Services.PdfExport.CreateFromFilesAsync(paths);
            var file = await Services.Export.SaveBytesAsAsync(this, pdf, "PDF document", ".pdf", "Magic Capture Desktop multi-page");
            if (file is not null) UtilityOutputBox.Text = $"Saved {paths.Length} PDF page(s): {file.Path}\r\n{pdf.LongLength:N0} bytes";
        }
        catch (Exception ex) { Services.Log.Error("UtilityMultiPdf", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityContactSheetPdf_Click(object sender, RoutedEventArgs e)
    {
        var assets = await TryLoadSelectedHistoryAssetsAsync(100, "UtilityContactSheetSelection");
        if (assets is null) return;
        if (assets.Count == 0) { UtilityOutputBox.Text = "Select one or more captures in History first."; return; }
        try
        {
            var pdf = await Task.Run(() => Services.PdfExport.CreateContactSheet(assets.Select(a => a.PngBytes).ToArray()));
            var file = await Services.Export.SaveBytesAsAsync(this, pdf, "PDF contact sheet", ".pdf", "Magic Capture Desktop contact sheet");
            if (file is not null) UtilityOutputBox.Text = $"Contact sheet saved: {file.Path}\r\n{pdf.LongLength:N0} bytes";
        }
        catch (Exception ex) { Services.Log.Error("UtilityContactPdf", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityCopyDataUri_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        const string prefix = "data:image/png;base64,";
        try
        {
            Base64ClipboardPolicy.ValidateSourceLength(asset.PngBytes.LongLength, prefix.Length);
            Services.Clipboard.CopyText(prefix + Convert.ToBase64String(asset.PngBytes));
            UtilityOutputBox.Text = $"PNG Data URI copied ({asset.PngBytes.LongLength:N0} source bytes).";
        }
        catch (InvalidDataException ex)
        {
            Services.Log.Error("UtilityCopyDataUri", ex);
            UtilityOutputBox.Text = ex.Message;
        }
    }

    private async void UtilityCopyBase64_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        try
        {
            Base64ClipboardPolicy.ValidateSourceLength(asset.PngBytes.LongLength);
            Services.Clipboard.CopyText(Convert.ToBase64String(asset.PngBytes));
            UtilityOutputBox.Text = $"PNG Base64 copied ({asset.PngBytes.LongLength:N0} source bytes).";
        }
        catch (InvalidDataException ex)
        {
            Services.Log.Error("UtilityCopyBase64", ex);
            UtilityOutputBox.Text = ex.Message;
        }
    }

    private async void UtilityCopyFile_Click(object sender, RoutedEventArgs e)
    {
        var path = FirstSelectedHistoryPath();
        if (path is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        try { await Services.Clipboard.CopyFileAsync(path); UtilityOutputBox.Text = $"Image file copied to clipboard:\r\n{path}"; }
        catch (Exception ex) { Services.Log.Error("UtilityCopyFile", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private void UtilityCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var path = FirstSelectedHistoryPath();
        if (path is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        Services.Clipboard.CopyText(path);
        UtilityOutputBox.Text = $"Path copied:\r\n{path}";
    }

    private void UtilityCopyFolderPath_Click(object sender, RoutedEventArgs e)
    {
        var path = FirstSelectedHistoryPath();
        if (path is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var folder = Path.GetDirectoryName(path) ?? path;
        Services.Clipboard.CopyText(folder);
        UtilityOutputBox.Text = $"Folder path copied:\r\n{folder}";
    }

    private async void UtilityPinClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var png = await ClipboardImageReader.ReadPngAsync();
            if (png is null) { UtilityOutputBox.Text = "Clipboard does not contain an image."; return; }
            using var bitmap = BitmapCodec.Decode(png);
            var asset = CaptureAsset.Create(new PixelRect(0, 0, bitmap.Width, bitmap.Height), png, CaptureSourceKind.Imported, "Clipboard");
            ((App)Application.Current).OpenPin(asset);
            UtilityOutputBox.Text = $"Pinned clipboard image {bitmap.Width}×{bitmap.Height}.";
        }
        catch (Exception ex) { Services.Log.Error("PinClipboard", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityPinImageFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await CreateImagePicker().PickSingleFileAsync();
            if (file is null) return;
            var bytes = await ImageFileReader.ReadAsync(file.Path);
            using var bitmap = BitmapCodec.DecodeForPixelProcessing(bytes);
            var png = BitmapCodec.EncodePng(bitmap);
            var asset = CaptureAsset.Create(new PixelRect(0, 0, bitmap.Width, bitmap.Height), png, CaptureSourceKind.Imported, file.Name);
            ((App)Application.Current).OpenPin(asset);
            UtilityOutputBox.Text = $"Pinned {file.Name} ({bitmap.Width}×{bitmap.Height}).";
        }
        catch (Exception ex) { Services.Log.Error("PinFile", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityOptimizeJpeg_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var quality = new NumberBox { Header = "Maximum JPEG quality", Minimum = 1, Maximum = 100, Value = 90, Width = 220 };
        var target = new NumberBox { Header = "Target size (KB)", Minimum = 16, Maximum = 262144, Value = 1000, Width = 220 };
        var targetMode = new CheckBox { Content = "Fit under target size (may resize only if quality is not enough)", IsChecked = true };
        var panel = new StackPanel { Spacing = 8, MinWidth = 460 };
        panel.Children.Add(quality); panel.Children.Add(target); panel.Children.Add(targetMode);
        var dialog = new ContentDialog { Title = "JPEG compressor", Content = panel, PrimaryButtonText = "Compress & save", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var q = double.IsFinite(quality.Value) ? (int)Math.Round(quality.Value) : 90;
            ImageOptimizationResult result;
            if (targetMode.IsChecked == true)
            {
                var kb = double.IsFinite(target.Value) ? Math.Max(16, target.Value) : 1000;
                var policy = new Magic.Capture.Core.Export.ImageOptimizationPolicy((long)Math.Round(kb * 1024), q, Math.Min(25, q));
                result = await Task.Run(() => Services.ImageOptimization.CompressJpegToTarget(asset.PngBytes, policy));
            }
            else result = await Task.Run(() => Services.ImageOptimization.CompressJpeg(asset.PngBytes, q));
            var file = await Services.Export.SaveBytesAsAsync(this, result.Bytes, "JPEG image", ".jpg", "Magic Capture Desktop optimized");
            UtilityOutputBox.Text = OptimizationSummary(result, file?.Path);
        }
        catch (Exception ex) { Services.Log.Error("UtilityOptimizeJpeg", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityOptimizePngLossless_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        try
        {
            var result = await Task.Run(() => Services.ImageOptimization.OptimizePngLossless(asset.PngBytes));
            var file = await Services.Export.SaveBytesAsAsync(this, result.Bytes, "PNG image", ".png", "Magic Capture Desktop optimized");
            UtilityOutputBox.Text = OptimizationSummary(result, file?.Path);
        }
        catch (Exception ex) { Services.Log.Error("UtilityOptimizePng", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityOptimizePngLossy_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var bits = new NumberBox { Header = "Color bits per RGB channel (3–8)", Minimum = 3, Maximum = 8, Value = 6, Width = 280 };
        var dialog = new ContentDialog { Title = "Lossy PNG optimization", Content = bits, PrimaryButtonText = "Optimize & save", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var channelBits = double.IsFinite(bits.Value) ? (int)Math.Round(bits.Value) : 6;
            var result = await Task.Run(() => Services.ImageOptimization.OptimizePngLossy(asset.PngBytes, channelBits));
            var file = await Services.Export.SaveBytesAsAsync(this, result.Bytes, "PNG image", ".png", "Magic Capture Desktop lossy");
            UtilityOutputBox.Text = OptimizationSummary(result, file?.Path) + $"\r\nRGB channel precision: {Math.Clamp(channelBits, 3, 8)} bits";
        }
        catch (Exception ex) { Services.Log.Error("UtilityLossyPng", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityResize_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        var width = new NumberBox { Header = "Width", Minimum = 1, Maximum = 32768, Value = asset.Width, Width = 180 };
        var height = new NumberBox { Header = "Height", Minimum = 1, Maximum = 32768, Value = asset.Height, Width = 180 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; row.Children.Add(width); row.Children.Add(height);
        var dialog = new ContentDialog { Title = "Resize image", Content = row, PrimaryButtonText = "Resize", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var w = double.IsFinite(width.Value) ? (int)Math.Round(width.Value) : asset.Width;
        var h = double.IsFinite(height.Value) ? (int)Math.Round(height.Value) : asset.Height;
        try
        {
            var bytes = await Task.Run(() => Services.ImageOptimization.Resize(asset.PngBytes, w, h));
            ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, $"Resized {w}×{h}"));
            UtilityOutputBox.Text = $"Resized {asset.Width}×{asset.Height} → {w}×{h}.";
        }
        catch (Exception ex) { Services.Log.Error("UtilityResize", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityEffectPipeline_Click(object sender, RoutedEventArgs e)
    {
        var selections = SelectedHistoryDisplays(maximumCount: 500).ToArray();
        if (selections.Length == 0) { UtilityOutputBox.Text = "Select one or more captures in History first."; return; }

        var steps = _lastEffectPipeline.Normalize().Steps.ToList();
        var preset = new ComboBox { Header = "Built-in preset", ItemsSource = ImageEffectPresets.BuiltIn, DisplayMemberPath = "Name", MinWidth = 220 };
        var kind = new ComboBox { Header = "Effect", ItemsSource = Enum.GetValues<ImageEffectKind>(), SelectedItem = ImageEffectKind.Brightness, MinWidth = 190 };
        var amount = new NumberBox { Header = "Amount", Minimum = -100, Maximum = 100, Value = 0, Width = 150 };
        var amount2 = new NumberBox { Header = "Green", Minimum = -100, Maximum = 100, Value = 0, Width = 120, Visibility = Visibility.Collapsed };
        var amount3 = new NumberBox { Header = "Blue", Minimum = -100, Maximum = 100, Value = 0, Width = 120, Visibility = Visibility.Collapsed };
        var add = new Button { Content = "Add step" };
        var remove = new Button { Content = "Remove selected" };
        var clear = new Button { Content = "Clear" };
        var stepList = new ListView { SelectionMode = ListViewSelectionMode.Single, MinHeight = 150, MaxHeight = 260 };
        var batch = new CheckBox { Content = $"Apply to all {selections.Length} selected capture(s) and export PNG files", IsChecked = selections.Length > 1 };

        void RefreshSteps() => stepList.ItemsSource = steps.Select((step, index) =>
        {
            var normalized = step.Normalize();
            return normalized.Kind == ImageEffectKind.ColorBalance
                ? $"{index + 1}. {normalized.Kind} · R {normalized.Amount:0.##} · G {normalized.SecondaryAmount:0.##} · B {normalized.TertiaryAmount:0.##}"
                : $"{index + 1}. {normalized.Kind} · {normalized.Amount:0.##}";
        }).ToArray();
        void SetAmountRange(ImageEffectKind selected)
        {
            amount2.Visibility = selected == ImageEffectKind.ColorBalance ? Visibility.Visible : Visibility.Collapsed;
            amount3.Visibility = selected == ImageEffectKind.ColorBalance ? Visibility.Visible : Visibility.Collapsed;
            amount.Header = selected == ImageEffectKind.ColorBalance ? "Red" : "Amount";
            switch (selected)
            {
                case ImageEffectKind.Gamma: amount.Minimum = 0.1; amount.Maximum = 5; amount.Value = 1; break;
                case ImageEffectKind.Exposure: amount.Minimum = -4; amount.Maximum = 4; amount.Value = 0; break;
                case ImageEffectKind.Hue: amount.Minimum = -180; amount.Maximum = 180; amount.Value = 0; break;
                case ImageEffectKind.Sharpen: amount.Minimum = 0; amount.Maximum = 5; amount.Value = 1; break;
                case ImageEffectKind.NoiseReduction: amount.Minimum = 1; amount.Maximum = 4; amount.Value = 1; break;
                case ImageEffectKind.EdgeDetection: amount.Minimum = 0.1; amount.Maximum = 5; amount.Value = 1; break;
                case ImageEffectKind.Posterize: amount.Minimum = 2; amount.Maximum = 32; amount.Value = 8; break;
                case ImageEffectKind.Threshold: amount.Minimum = 0; amount.Maximum = 255; amount.Value = 128; break;
                case ImageEffectKind.Mosaic: amount.Minimum = 2; amount.Maximum = 64; amount.Value = 8; break;
                default: amount.Minimum = -100; amount.Maximum = 100; amount.Value = 0; break;
            }
        }

        SetAmountRange(ImageEffectKind.Brightness);
        RefreshSteps();
        kind.SelectionChanged += (_, _) => { if (kind.SelectedItem is ImageEffectKind selected) SetAmountRange(selected); };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not ImageEffectPreset selected) return;
            steps.Clear(); steps.AddRange(selected.Pipeline.Normalize().Steps); RefreshSteps();
        };
        add.Click += (_, _) =>
        {
            if (steps.Count >= 32 || kind.SelectedItem is not ImageEffectKind selected) return;
            var value = double.IsFinite(amount.Value) ? amount.Value : 0;
            var value2 = double.IsFinite(amount2.Value) ? amount2.Value : 0;
            var value3 = double.IsFinite(amount3.Value) ? amount3.Value : 0;
            steps.Add(new ImageEffectStep(selected, value, value2, value3).Normalize()); RefreshSteps();
        };
        remove.Click += (_, _) => { if (stepList.SelectedIndex is var index && index >= 0 && index < steps.Count) { steps.RemoveAt(index); RefreshSteps(); } };
        clear.Click += (_, _) => { steps.Clear(); preset.SelectedItem = null; RefreshSteps(); };

        var controls = new StackPanel { Spacing = 8, MinWidth = 560 };
        controls.Children.Add(preset);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; row.Children.Add(kind); row.Children.Add(amount); row.Children.Add(amount2); row.Children.Add(amount3); row.Children.Add(add); controls.Children.Add(row);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(remove); actions.Children.Add(clear); controls.Children.Add(actions);
        controls.Children.Add(stepList); controls.Children.Add(batch);
        var dialog = new ContentDialog { Title = "Image effect pipeline", Content = controls, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || steps.Count == 0) return;
        var pipeline = new ImageEffectPipeline(steps.ToArray()).Normalize();
        _lastEffectPipeline = pipeline;

        try
        {
            if (batch.IsChecked != true)
            {
                var asset = await LoadHistoryAssetAsync(selections[0]);
                if (asset is null) { UtilityOutputBox.Text = "The selected capture could not be loaded."; return; }
                var bytes = await Task.Run(() => Services.ImageEffects.Apply(asset.PngBytes, pipeline));
                ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, "Effect pipeline"));
                UtilityOutputBox.Text = $"Applied {pipeline.Steps.Count} effect step(s) locally.";
                return;
            }

            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary }; folderPicker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, Platform.WindowHelpers.GetWindowHandle(this));
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;
            var succeeded = 0; var failed = 0;
            for (var i = 0; i < selections.Length; i++)
            {
                try
                {
                    var asset = await LoadHistoryAssetAsync(selections[i]);
                    if (asset is null) { failed++; continue; }
                    var bytes = await Task.Run(() => Services.ImageEffects.Apply(asset.PngBytes, pipeline));
                    var path = GetCollisionSafePath(folder.Path, $"effects-{i + 1:000}.png");
                    await File.WriteAllBytesAsync(path, bytes); succeeded++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                { failed++; Services.Log.Error("UtilityEffectPipelineBatch", ex); }
            }
            UtilityOutputBox.Text = $"Effect batch complete: {succeeded} saved, {failed} failed · {pipeline.Steps.Count} step(s).";
        }
        catch (Exception ex) { Services.Log.Error("UtilityEffectPipeline", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityImportEffectPack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pack = await ImportEffectPackAsync();
            if (pack is null) return;
            _lastEffectPipeline = pack.Pipeline.Normalize();
            UtilityOutputBox.Text = $"Imported effect pack '{pack.Name}' with {_lastEffectPipeline.Steps.Count} step(s). Open Effect pipeline… to preview/apply it.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            Services.Log.Error("ImportEffectPack", ex);
            UtilityOutputBox.Text = ex.Message;
        }
    }

    private async void UtilityExportEffectPack_Click(object sender, RoutedEventArgs e)
    {
        try { await ExportEffectPackAsync(); }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            Services.Log.Error("ExportEffectPack", ex);
            UtilityOutputBox.Text = ex.Message;
        }
    }

    private async Task<ImageEffectPack?> ImportEffectPackAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".magiceffect");
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size == 0 || properties.Size > ImageEffectPackSerializer.MaximumJsonBytes)
            throw new InvalidDataException($"Effect pack must be between 1 and {ImageEffectPackSerializer.MaximumJsonBytes:N0} bytes.");
        var json = await File.ReadAllTextAsync(file.Path);
        return ImageEffectPackSerializer.Deserialize(json);
    }

    private async Task ExportEffectPackAsync()
    {
        var nameBox = new TextBox { Header = "Pack name", Text = "My effects", MaxLength = ImageEffectPackSerializer.MaximumNameCharacters, MinWidth = 360 };
        var dialog = new ContentDialog { Title = "Export effect pack", Content = nameBox, PrimaryButtonText = "Save", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var json = ImageEffectPackSerializer.Serialize(nameBox.Text, _lastEffectPipeline);
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "magic-effects" };
        picker.FileTypeChoices.Add("Magic Capture effect pack", new List<string> { ".magiceffect" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await File.WriteAllTextAsync(file.Path, json);
        UtilityOutputBox.Text = $"Exported {_lastEffectPipeline.Normalize().Steps.Count} effect step(s) to {file.Name}.";
    }

    private async void UtilityCanvasEffects_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }

        string[] operations =
        [
            "Border · Simple", "Border · Double", "Border · Photo", "Border · Dark",
            "Torn edges", "Fade edges", "Reflection", "Watermark text", "Watermark image/logo",
            "Date/time stamp", "Capture information stamp", "Auto-crop plain borders", "Expand canvas",
            "Make background transparent", "Color-key removal", "Rotate arbitrary"
        ];
        var operation = new ComboBox { Header = "Operation", ItemsSource = operations, SelectedIndex = 0, MinWidth = 260 };
        var amount = new NumberBox { Header = "Amount", Minimum = -360, Maximum = 4096, Value = 16, Width = 170 };
        var opacity = new NumberBox { Header = "Opacity %", Minimum = 1, Maximum = 100, Value = 70, Width = 160 };
        var color = new TextBox { Header = "Color #AARRGGBB / #RRGGBB", Text = "#FFFFFFFF", MaxLength = 9, MinWidth = 220 };
        var text = new TextBox { Header = "Watermark text", MaxLength = 512, MinWidth = 420 };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

        void RefreshCanvasControls()
        {
            var selected = operation.SelectedItem?.ToString() ?? operations[0];
            amount.Visibility = Visibility.Visible; opacity.Visibility = Visibility.Collapsed; color.Visibility = Visibility.Collapsed; text.Visibility = Visibility.Collapsed;
            hint.Text = "All operations are local and processed on demand.";
            switch (selected)
            {
                case "Torn edges": amount.Header = "Edge depth (px)"; amount.Minimum = 2; amount.Maximum = 1024; amount.Value = 24; break;
                case "Fade edges": amount.Header = "Fade depth (px)"; amount.Minimum = 1; amount.Maximum = 1024; amount.Value = 32; break;
                case "Reflection": amount.Header = "Reflection height %"; amount.Minimum = 5; amount.Maximum = 75; amount.Value = 30; opacity.Visibility = Visibility.Visible; opacity.Value = 45; break;
                case "Watermark text": amount.Header = "Font size (px)"; amount.Minimum = 8; amount.Maximum = 160; amount.Value = 24; opacity.Visibility = Visibility.Visible; text.Visibility = Visibility.Visible; break;
                case "Watermark image/logo": amount.Header = "Logo width %"; amount.Minimum = 5; amount.Maximum = 80; amount.Value = 20; opacity.Visibility = Visibility.Visible; hint.Text = "After Apply, choose a local image/logo file."; break;
                case "Auto-crop plain borders": amount.Header = "Color tolerance"; amount.Minimum = 0; amount.Maximum = 64; amount.Value = 8; break;
                case "Expand canvas": amount.Header = "Padding (px)"; amount.Minimum = 1; amount.Maximum = 4096; amount.Value = 32; color.Visibility = Visibility.Visible; color.Text = "#00000000"; break;
                case "Make background transparent": amount.Header = "Tolerance"; amount.Minimum = 1; amount.Maximum = 255; amount.Value = 16; color.Visibility = Visibility.Visible; break;
                case "Color-key removal": amount.Header = "Tolerance"; amount.Minimum = 0; amount.Maximum = 255; amount.Value = 0; color.Visibility = Visibility.Visible; break;
                case "Rotate arbitrary": amount.Header = "Degrees"; amount.Minimum = -360; amount.Maximum = 360; amount.Value = 15; color.Visibility = Visibility.Visible; color.Text = "#00000000"; break;
                default: amount.Visibility = Visibility.Collapsed; break;
            }
        }
        operation.SelectionChanged += (_, _) => RefreshCanvasControls();
        RefreshCanvasControls();
        var controls = new StackPanel { Spacing = 8, MinWidth = 540 };
        controls.Children.Add(operation);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; row.Children.Add(amount); row.Children.Add(opacity); controls.Children.Add(row);
        controls.Children.Add(color); controls.Children.Add(text); controls.Children.Add(hint);
        var dialog = new ContentDialog { Title = "Advanced canvas effects", Content = controls, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var selected = operation.SelectedItem?.ToString() ?? operations[0];
            var value = double.IsFinite(amount.Value) ? amount.Value : 0;
            var opacityValue = double.IsFinite(opacity.Value) ? (int)Math.Round(opacity.Value) : 70;
            var argb = TryParseArgbHex(color.Text, out var parsed) ? parsed : 0xFFFFFFFF;
            byte[] result;
            switch (selected)
            {
                case "Border · Simple": result = Services.ImageCanvasOperations.AddBorderPreset(asset.PngBytes, ImageBorderPreset.Simple); break;
                case "Border · Double": result = Services.ImageCanvasOperations.AddBorderPreset(asset.PngBytes, ImageBorderPreset.Double); break;
                case "Border · Photo": result = Services.ImageCanvasOperations.AddBorderPreset(asset.PngBytes, ImageBorderPreset.Photo); break;
                case "Border · Dark": result = Services.ImageCanvasOperations.AddBorderPreset(asset.PngBytes, ImageBorderPreset.Dark); break;
                case "Torn edges": result = Services.ImageCanvasOperations.TornEdges(asset.PngBytes, (int)Math.Round(value)); break;
                case "Fade edges": result = Services.ImageCanvasOperations.FadeEdges(asset.PngBytes, (int)Math.Round(value)); break;
                case "Reflection": result = Services.ImageCanvasOperations.AddReflection(asset.PngBytes, (int)Math.Round(value), opacityValue); break;
                case "Watermark text": result = Services.ImageCanvasOperations.AddTextWatermark(asset.PngBytes, text.Text, (int)Math.Round(value), opacityValue); break;
                case "Watermark image/logo":
                {
                    var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }) picker.FileTypeFilter.Add(ext);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
                    var file = await picker.PickSingleFileAsync();
                    if (file is null) return;
                    var watermark = await ImageFileReader.ReadAsync(file.Path);
                    result = Services.ImageCanvasOperations.AddImageWatermark(asset.PngBytes, watermark, (int)Math.Round(value), opacityValue);
                    break;
                }
                case "Date/time stamp": result = Services.ImageCanvasOperations.AddDateTimeStamp(asset.PngBytes, DateTimeOffset.Now); break;
                case "Capture information stamp":
                {
                    var info = $"{asset.SourceKind} · {asset.Width}×{asset.Height} · {asset.ProcessName ?? asset.SourceDisplayName ?? "Desktop"} · {asset.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
                    result = Services.ImageCanvasOperations.AddCaptureInformationStamp(asset.PngBytes, info); break;
                }
                case "Auto-crop plain borders": result = Services.ImageCanvasOperations.AutoCropPlainBorders(asset.PngBytes, (int)Math.Round(value)); break;
                case "Expand canvas":
                    if (!TryParseArgbHex(color.Text, out argb)) throw new FormatException("Enter a valid #AARRGGBB or #RRGGBB color.");
                    result = Services.ImageCanvasOperations.ExpandCanvas(asset.PngBytes, (int)Math.Round(value), argb); break;
                case "Make background transparent":
                    if (!TryParseArgbHex(color.Text, out argb)) throw new FormatException("Enter a valid #AARRGGBB or #RRGGBB color.");
                    result = Services.ImageCanvasOperations.MakeColorTransparent(asset.PngBytes, argb, Math.Max(1, (int)Math.Round(value))); break;
                case "Color-key removal":
                    if (!TryParseArgbHex(color.Text, out argb)) throw new FormatException("Enter a valid #AARRGGBB or #RRGGBB color.");
                    result = Services.ImageCanvasOperations.MakeColorTransparent(asset.PngBytes, argb, (int)Math.Round(value)); break;
                case "Rotate arbitrary":
                    if (!TryParseArgbHex(color.Text, out argb)) throw new FormatException("Enter a valid #AARRGGBB or #RRGGBB color.");
                    result = Services.ImageCanvasOperations.RotateArbitrary(asset.PngBytes, value, argb); break;
                default: throw new InvalidOperationException("Unknown canvas operation.");
            }
            ((App)Application.Current).OpenResult(CreateDerivedAsset(result, selected));
            UtilityOutputBox.Text = $"Applied {selected} locally.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            Services.Log.Error("UtilityCanvasEffects", ex);
            UtilityOutputBox.Text = ex.Message;
        }
    }

    private static bool TryParseArgbHex(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var raw = text.Trim().TrimStart('#');
        if (raw.Length == 6) raw = "FF" + raw;
        return raw.Length == 8 && uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private async void UtilityBatchOptimize_Click(object sender, RoutedEventArgs e)
    {
        var selections = SelectedHistoryDisplays(maximumCount: 500).ToArray();
        if (selections.Length == 0) { UtilityOutputBox.Text = "Select one or more captures in History first."; return; }
        var target = new NumberBox { Header = "Target size per JPEG (KB)", Minimum = 16, Maximum = 262144, Value = 1000, Width = 240 };
        var quality = new NumberBox { Header = "Maximum quality", Minimum = 1, Maximum = 100, Value = 90, Width = 200 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; panel.Children.Add(target); panel.Children.Add(quality);
        var dialog = new ContentDialog { Title = $"Batch optimize {selections.Length} image(s)", Content = panel, PrimaryButtonText = "Choose output folder", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary }; folderPicker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, Platform.WindowHelpers.GetWindowHandle(this));
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;
        var targetKb = double.IsFinite(target.Value) ? Math.Max(16, target.Value) : 1000;
        var q = double.IsFinite(quality.Value) ? (int)Math.Round(quality.Value) : 90;
        var policy = new Magic.Capture.Core.Export.ImageOptimizationPolicy((long)Math.Round(targetKb * 1024), q, Math.Min(25, q));
        var succeeded = 0; var failed = 0; long before = 0; long after = 0;
        for (var i = 0; i < selections.Length; i++)
        {
            try
            {
                var asset = await LoadHistoryAssetAsync(selections[i]);
                if (asset is null) { failed++; continue; }
                before += asset.PngBytes.LongLength;
                var result = await Task.Run(() => Services.ImageOptimization.CompressJpegToTarget(asset.PngBytes, policy));
                var destination = GetCollisionSafePath(folder.Path, $"optimized-{i + 1:000}.jpg");
                await File.WriteAllBytesAsync(destination, result.Bytes);
                after += result.Bytes.LongLength; succeeded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            { failed++; Services.Log.Error("UtilityBatchOptimize", ex); }
        }
        UtilityOutputBox.Text = $"Batch JPEG complete: {succeeded} saved, {failed} failed.\r\nBefore: {before:N0} bytes\r\nAfter: {after:N0} bytes\r\nSaved: {Math.Max(0, before - after):N0} bytes";
    }

    private async void UtilityQrGenerator_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { Header = "QR content", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 480, Height = 120, MaxLength = GeneratedCodeInputPolicy.MaximumQrUtf8Bytes };
        var dialog = new ContentDialog { Title = "QR generator", Content = text, PrimaryButtonText = "Generate", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(text.Text)) return;
        try { var bytes = Services.BarcodeGenerator.GenerateQr(text.Text); ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, "Generated QR")); UtilityOutputBox.Text = "QR code generated locally."; }
        catch (Exception ex) { Services.Log.Error("UtilityQrGenerator", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityBarcodeGenerator_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { Header = "Code 128 content", MinWidth = 480, MaxLength = GeneratedCodeInputPolicy.MaximumCode128Characters };
        var dialog = new ContentDialog { Title = "Barcode generator", Content = text, PrimaryButtonText = "Generate", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(text.Text)) return;
        try { var bytes = Services.BarcodeGenerator.GenerateCode128(text.Text); ((App)Application.Current).OpenResult(CreateDerivedAsset(bytes, "Generated Code 128")); UtilityOutputBox.Text = "Code 128 barcode generated locally."; }
        catch (Exception ex) { Services.Log.Error("UtilityBarcodeGenerator", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityFileHashCompare_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;
        var selected = files.Take(2).ToArray();
        try
        {
            var rows = new List<string>();
            var hashes = new List<string>(selected.Length);
            foreach (var file in selected)
            {
                var hash = await HashUtility.ComputeFileSha256Async(file.Path);
                hashes.Add(hash);
                var info = new FileInfo(file.Path);
                rows.Add($"{file.Name}\r\n  {info.Length:N0} bytes\r\n  SHA-256 {hash}");
            }
            if (hashes.Count == 2)
                rows.Add(hashes[0] == hashes[1] ? "Result: files have identical SHA-256 hashes." : "Result: files differ.");
            UtilityOutputBox.Text = string.Join("\r\n\r\n", rows);
        }
        catch (Exception ex) { Services.Log.Error("UtilityFileHashCompare", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityDirectoryIndex_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        try
        {
            var index = await Task.Run(() => BuildDirectoryIndex(folder.Path, maximumEntries: 20_000, maximumDepth: 16));
            UtilityOutputBox.Text = index.Length <= 16_000 ? index : index[..16_000] + "\r\n… preview truncated …";
            await Services.Export.SaveTextAsAsync(this, index, "Markdown directory index", ".md");
        }
        catch (Exception ex) { Services.Log.Error("UtilityDirectoryIndex", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityClipboardViewer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            var formats = content.AvailableFormats.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Formats:");
            foreach (var format in formats.Take(64)) builder.AppendLine("  " + format);
            if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                builder.AppendLine().AppendLine("Text preview:");
                if (ClipboardTextPreviewReader.TryRead(out var preview, out var truncated))
                    builder.Append(preview).Append(truncated ? "…" : string.Empty);
                else
                    builder.Append("Text preview is temporarily unavailable (clipboard is busy or the text format could not be read safely).");
            }
            UtilityOutputBox.Text = builder.ToString();
        }
        catch (Exception ex) { Services.Log.Error("UtilityClipboardViewer", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private void UtilityDesignTools_Click(object sender, RoutedEventArgs e)
    {
        var window = new DesignToolsWindow(Services);
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private void UtilityWindowInspector_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var windows = Services.WindowCapture.ListCapturableWindows();
            UtilityOutputBox.Text = windows.Count == 0 ? "No capturable windows found." : string.Join("\r\n\r\n", windows.Select(window =>
                $"HWND 0x{window.Handle.ToInt64():X}\r\nProcess: {window.ProcessName ?? "?"}\r\nClass: {window.ClassName ?? "?"}\r\nBounds: {window.Bounds.X},{window.Bounds.Y} {window.Bounds.Width}×{window.Bounds.Height}\r\nTitle: {window.Title}"));
        }
        catch (Exception ex) { Services.Log.Error("UtilityWindowInspector", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityMonitorTest_Click(object sender, RoutedEventArgs e)
    {
        var monitors = Services.Monitors.ListMonitors();
        if (monitors.Count == 0) { UtilityOutputBox.Text = "No monitor is currently available."; return; }
        var list = new ListView { ItemsSource = monitors, SelectionMode = ListViewSelectionMode.Single, SelectedIndex = 0, MinWidth = 520, MaxHeight = 360 };
        var dialog = new ContentDialog { Title = "Choose monitor test target", Content = list, PrimaryButtonText = "Open test", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not MonitorInfo monitor) return;
        var window = new MonitorTestWindow(monitor);
        ((App)Application.Current).TrackChildWindow(window);
        window.ShowOnMonitor();
        UtilityOutputBox.Text = "Monitor test opened. Use solid colors for dead pixels, gradient for banding and color bars for calibration.";
    }

    private async void UtilityPixelStatistics_Click(object sender, RoutedEventArgs e)
    {
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { UtilityOutputBox.Text = "Select a capture in History first."; return; }
        try
        {
            var stats = await Task.Run(() => Services.PixelStatistics.Compute(asset.PngBytes));
            UtilityOutputBox.Text =
                $"Pixels: {stats.PixelCount:N0} · {stats.Width}×{stats.Height}\r\n" +
                $"Mean RGBA: {stats.MeanRed:0.##}, {stats.MeanGreen:0.##}, {stats.MeanBlue:0.##}, {stats.MeanAlpha:0.##}\r\n" +
                $"RGB range: R {stats.MinimumRed}–{stats.MaximumRed} · G {stats.MinimumGreen}–{stats.MaximumGreen} · B {stats.MinimumBlue}–{stats.MaximumBlue}\r\n" +
                $"Fully opaque pixels: {stats.OpaquePixelPercent:0.##}%";
        }
        catch (Exception ex) { Services.Log.Error("UtilityPixelStatistics", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private async void UtilityExternalEditor_Click(object sender, RoutedEventArgs e)
    {
        var imagePath = FirstSelectedHistoryPath();
        if (imagePath is null) { UtilityOutputBox.Text = "Select a History capture first."; return; }
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var editor = await picker.PickSingleFileAsync();
        if (editor is null) return;
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = editor.Path,
                WorkingDirectory = Path.GetDirectoryName(editor.Path) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(imagePath);
            using var process = Process.Start(start);
            if (process is null) throw new InvalidOperationException("The selected editor could not be started.");
            UtilityOutputBox.Text = $"Opened in external editor:\r\n{editor.Path}\r\n\r\nImage:\r\n{imagePath}";
        }
        catch (Exception ex) { Services.Log.Error("UtilityExternalEditor", ex); UtilityOutputBox.Text = ex.Message; }
    }

    private string? FirstSelectedHistoryPath()
    {
        var display = ActiveSelectedHistoryDisplays().FirstOrDefault();
        if (display is null) return null;
        try
        {
            var path = Services.HistoryStore.GetAbsolutePath(display.Item);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) { Services.Log.Error("HistorySelectedPath", ex); return null; }
    }

    private static string OptimizationSummary(ImageOptimizationResult result, string? path)
    {
        var target = result.TargetMet ? "target met" : "best effort; target not reached";
        return $"{result.Width}×{result.Height}" + (result.JpegQuality is { } q ? $" · JPEG quality {q}" : string.Empty) +
               $"\r\nBefore: {result.OriginalBytes:N0} bytes\r\nAfter: {result.Bytes.LongLength:N0} bytes\r\nSaved: {result.SavedBytes:N0} bytes ({result.SavedPercent:0.#}%)\r\n{target}" +
               (string.IsNullOrWhiteSpace(path) ? string.Empty : $"\r\nSaved: {path}");
    }

    private static string BuildDirectoryIndex(string root, int maximumEntries, int maximumDepth)
    {
        maximumEntries = Math.Clamp(maximumEntries, 1, DirectoryIndexPolicy.MaximumEntries);
        maximumDepth = Math.Clamp(maximumDepth, 1, DirectoryIndexPolicy.MaximumDepth);
        var builder = new System.Text.StringBuilder(capacity: Math.Min(64 * 1024, DirectoryIndexPolicy.MaximumOutputCharacters));
        var outputLimitReached = false;

        bool TryAppendLine(string line = "")
        {
            var extra = checked(line.Length + Environment.NewLine.Length);
            if (!DirectoryIndexPolicy.CanAppend(builder.Length, extra))
            {
                outputLimitReached = true;
                return false;
            }
            builder.AppendLine(line);
            return true;
        }

        TryAppendLine($"# Directory index — {DirectoryIndexPolicy.NormalizeDisplayName(Path.GetFileName(root))}");
        TryAppendLine();
        var count = 0;
        void Walk(string folder, int depth)
        {
            if (depth > maximumDepth || count >= maximumEntries || outputLimitReached) return;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (count >= maximumEntries || outputLimitReached) break;
                    count++;
                    var indent = new string(' ', depth * 2);
                    var name = DirectoryIndexPolicy.NormalizeDisplayName(Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        if (!TryAppendLine($"{indent}- **{name}/**")) break;
                        try
                        {
                            if ((File.GetAttributes(entry) & System.IO.FileAttributes.ReparsePoint) == 0) Walk(entry, depth + 1);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                    else
                    {
                        long size = 0;
                        try { size = new FileInfo(entry).Length; }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        if (!TryAppendLine($"{indent}- {name} — {size:N0} bytes")) break;
                    }
                }
            }
            catch (UnauthorizedAccessException) { TryAppendLine($"{new string(' ', depth * 2)}- [access denied]"); }
            catch (IOException) { TryAppendLine($"{new string(' ', depth * 2)}- [unavailable]"); }
        }

        Walk(root, 0);
        if (count >= maximumEntries)
        {
            TryAppendLine();
            TryAppendLine($"> Index stopped at the safety limit of {maximumEntries:N0} entries.");
        }
        else if (outputLimitReached)
        {
            var marker = $"{Environment.NewLine}> Index stopped at the {DirectoryIndexPolicy.MaximumOutputCharacters / (1024 * 1024)} MB output safety limit.";
            if (DirectoryIndexPolicy.CanAppend(builder.Length, marker.Length)) builder.Append(marker);
        }
        return builder.ToString();
    }

    private IReadOnlyList<HistoryDisplayItem> SelectedHistoryDisplays(int maximumCount = 500)
    {
        maximumCount = Math.Clamp(maximumCount, 1, 500);
        return ActiveSelectedHistoryDisplays().Take(maximumCount).ToArray();
    }

    private async Task<CaptureAsset?> LoadHistoryAssetAsync(HistoryDisplayItem display, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var item = display.Item;
            var path = Services.HistoryStore.GetAbsolutePath(item);
            if (!File.Exists(path)) return null;
            var bytes = await ImageFileReader.ReadAsync(path, cancellationToken);
            ImageWorkloadLimits.ValidateEncodedLength(bytes.LongLength);
            _ = Enum.TryParse<CaptureSourceKind>(item.SourceKind, out var kind);
            return new CaptureAsset(item.Id, item.CreatedUtc, new Magic.Capture.Core.Geometry.PixelRect(0, 0, item.Width, item.Height), bytes, item.Width, item.Height, kind, item.SourceDisplayName ?? "History", item.WindowTitle, item.ProcessName, item.MonitorName, ExecutablePath: item.ExecutablePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Log.Error("HistorySelection", ex);
            return null;
        }
    }

    private async Task<CaptureAsset?> LoadHistoryAssetByIdAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty) return null;
        var item = (await Services.HistoryStore.ListAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Id == assetId);
        if (item is null) return null;
        try
        {
            var path = Services.HistoryStore.GetAbsolutePath(item);
            if (!File.Exists(path)) return null;
            var bytes = await ImageFileReader.ReadAsync(path, cancellationToken);
            ImageWorkloadLimits.ValidateEncodedLength(bytes.LongLength);
            _ = Enum.TryParse<CaptureSourceKind>(item.SourceKind, out var kind);
            return new CaptureAsset(item.Id, item.CreatedUtc, new Magic.Capture.Core.Geometry.PixelRect(0, 0, item.Width, item.Height), bytes, item.Width, item.Height, kind, item.SourceDisplayName ?? "History", item.WindowTitle, item.ProcessName, item.MonitorName, ExecutablePath: item.ExecutablePath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Log.Error("WorkflowResumeHistory", ex);
            return null;
        }
    }

    private async Task<IReadOnlyList<CaptureAsset>> SelectedHistoryAssetsAsync(int maximumCount = int.MaxValue, CancellationToken cancellationToken = default)
    {
        var displays = SelectedHistoryDisplays(maximumCount);
        var assets = new List<CaptureAsset>(displays.Count);
        long residentBytes = 0;
        foreach (var display in displays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = await LoadHistoryAssetAsync(display, cancellationToken);
            if (asset is null) continue;
            if (displays.Count > 1)
            {
                residentBytes = checked(residentBytes + asset.PngBytes.LongLength);
                ImageWorkloadLimits.ValidateResidentSelectionBytes(residentBytes);
            }
            assets.Add(asset);
        }
        return assets;
    }

    private async Task<IReadOnlyList<CaptureAsset>?> TryLoadSelectedHistoryAssetsAsync(int maximumCount, string operationName)
    {
        try
        {
            return await SelectedHistoryAssetsAsync(maximumCount);
        }
        catch (InvalidDataException ex)
        {
            Services.Log.Error(operationName, ex);
            UtilityOutputBox.Text = ex.Message;
            return null;
        }
        catch (OverflowException ex)
        {
            Services.Log.Error(operationName, ex);
            UtilityOutputBox.Text = "The selected images are too large to process safely in one operation.";
            return null;
        }
    }

    private static CaptureAsset CreateDerivedAsset(byte[] pngBytes, string sourceName)
    {
        using var bitmap = BitmapCodec.Decode(pngBytes);
        return CaptureAsset.Create(new Magic.Capture.Core.Geometry.PixelRect(0, 0, bitmap.Width, bitmap.Height), pngBytes, CaptureSourceKind.Region, sourceName);
    }

    private async Task RefreshDestinationsAsync()
    {
        if (_services is null || !Services.Entitlements.CanUse(ProductFeature.CustomDestinations)) return;
        try
        {
            var loaded = await Services.Destinations.LoadAsync();
            _destinationItems = loaded;
            _destinationsLoadHealthy = true;
            DestinationList.ItemsSource = _destinationItems;
        }
        catch (Exception ex)
        {
            _destinationsLoadHealthy = false;
            DestinationStatusText.Text = "Destination storage could not be loaded. Existing in-memory entries were kept; saving is disabled until reload succeeds.";
            Services.Log.Error("DestinationLoad", ex);
        }
    }

    private void DestinationNew_Click(object sender, RoutedEventArgs e)
    {
        _editingDestinationId = null;
        DestinationList.SelectedItem = null;
        DestinationNameBox.Text = string.Empty;
        DestinationEndpointBox.Text = "https://";
        DestinationMethodCombo.SelectedIndex = 1;
        DestinationBodyCombo.SelectedIndex = 2;
        DestinationHeadersBox.Text = string.Empty;
        DestinationQueryBox.Text = string.Empty;
        DestinationBodyTemplateBox.Text = string.Empty;
        DestinationResultPathBox.Text = string.Empty;
        DestinationSecretIdBox.Text = string.Empty;
        DestinationSecretValueBox.Password = string.Empty;
        DestinationStatusText.Text = "New destination.";
    }

    private void DestinationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DestinationList.SelectedItem is not CustomHttpDestination destination) return;
        _editingDestinationId = Guid.TryParse(destination.Id, out var id) ? id : null;
        DestinationNameBox.Text = destination.Name;
        DestinationEndpointBox.Text = destination.Endpoint.ToString();
        DestinationMethodCombo.SelectedIndex = destination.Method.ToUpperInvariant() switch { "GET" => 0, "POST" => 1, "PUT" => 2, "PATCH" => 3, _ => 1 };
        DestinationBodyCombo.SelectedIndex = destination.BodyKind switch { DestinationBodyKind.None => 0, DestinationBodyKind.Json => 1, _ => 2 };
        DestinationHeadersBox.Text = string.Join("\r\n", destination.Headers.Select(kv => $"{kv.Key}: {kv.Value}"));
        DestinationQueryBox.Text = string.Join("\r\n", destination.Query.Select(kv => $"{kv.Key}={kv.Value}"));
        DestinationBodyTemplateBox.Text = destination.BodyTemplate ?? string.Empty;
        DestinationResultPathBox.Text = destination.ResultJsonPath ?? string.Empty;
        DestinationSecretIdBox.Text = destination.SecretReference ?? string.Empty;
        DestinationSecretValueBox.Password = string.Empty;
    }

    private async void DestinationSave_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro(ProductFeature.CustomDestinations)) return;
        if (!_destinationsLoadHealthy)
        {
            DestinationStatusText.Text = "Destination storage is not safely loaded. Refresh the Destinations page successfully before saving changes.";
            return;
        }
        try
        {
            var destination = BuildDestinationFromUi();
            var profiles = _destinationItems.Where(p => p.Id != destination.Id).Append(destination).ToArray();
            await Services.Destinations.SaveAsync(profiles);
            if (!string.IsNullOrWhiteSpace(DestinationSecretIdBox.Text) && !string.IsNullOrWhiteSpace(DestinationSecretValueBox.Password))
                await Services.DestinationSecrets.SaveAsync(DestinationSecretIdBox.Text.Trim(), DestinationSecretValueBox.Password);
            DestinationSecretValueBox.Password = string.Empty;
            DestinationStatusText.Text = "Destination saved locally.";
            await RefreshDestinationsAsync();
            DestinationList.SelectedItem = _destinationItems.FirstOrDefault(p => p.Id == destination.Id);
        }
        catch (Exception ex) { DestinationStatusText.Text = ex.Message; Services.Log.Error("DestinationSave", ex); }
    }

    private async void DestinationDelete_Click(object sender, RoutedEventArgs e)
    {
        if (DestinationList.SelectedItem is not CustomHttpDestination selected) return;
        try
        {
            var workflows = await Services.Workflows.LoadAsync();
            var dependents = WorkflowReferencePolicy.FindDestinationDependents(selected.Id, workflows, _magicRecipes);
            if (dependents.Count > 0)
                throw new InvalidOperationException("Destination is still referenced by " + string.Join(", ", dependents) + ". Remove those references first.");
            await Services.Destinations.SaveAsync(_destinationItems.Where(p => p.Id != selected.Id));
            DestinationStatusText.Text = "Destination deleted.";
            DestinationNew_Click(sender, e);
            await RefreshDestinationsAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("DestinationDelete", ex);
            DestinationStatusText.Text = ex.Message;
        }
    }

    private async void DestinationTest_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro(ProductFeature.CustomDestinations)) return;
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { DestinationStatusText.Text = "Select a capture in History first."; return; }
        try
        {
            var destination = BuildDestinationFromUi();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["filename"] = "capture.png", ["width"] = asset.Width.ToString(), ["height"] = asset.Height.ToString(),
                ["source"] = asset.SourceDisplayName ?? "History", ["captureId"] = asset.Id.ToString("N"), ["workflow"] = "test", ["utc"] = DateTimeOffset.UtcNow.ToString("O")
            };
            var response = await Services.DestinationClient.SendAsync(destination, new DestinationRequestContext(asset, "capture.png", values));
            DestinationStatusText.Text = $"HTTP {response.StatusCode}" + (string.IsNullOrWhiteSpace(response.ResultUrl) ? string.Empty : $" · {response.ResultUrl}");
        }
        catch (Exception ex) { DestinationStatusText.Text = ex.Message; Services.Log.Error("DestinationTest", ex); }
    }

    private CustomHttpDestination BuildDestinationFromUi()
    {
        if (!Uri.TryCreate(DestinationEndpointBox.Text.Trim(), UriKind.Absolute, out var endpoint)) throw new InvalidOperationException("Enter a valid absolute endpoint URL.");
        var method = (DestinationMethodCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "POST";
        var body = (DestinationBodyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Multipart";
        var bodyKind = Enum.TryParse<DestinationBodyKind>(body, true, out var parsedBody) ? parsedBody : DestinationBodyKind.Multipart;
        var id = _editingDestinationId?.ToString("D") ?? Guid.NewGuid().ToString("D");
        var secretId = string.IsNullOrWhiteSpace(DestinationSecretIdBox.Text) ? null : DestinationSecretIdBox.Text.Trim();
        var destination = new CustomHttpDestination(id, DestinationNameBox.Text.Trim(), method, endpoint, bodyKind,
            ParseHeaderLines(DestinationHeadersBox.Text), ParseQueryLines(DestinationQueryBox.Text),
            string.IsNullOrWhiteSpace(DestinationBodyTemplateBox.Text) ? null : DestinationBodyTemplateBox.Text,
            bodyKind == DestinationBodyKind.Multipart ? "file" : null,
            string.IsNullOrWhiteSpace(DestinationResultPathBox.Text) ? null : DestinationResultPathBox.Text.Trim(),
            secretId, false);
        var validation = DestinationValidator.Validate(destination);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        return destination;
    }

    private static IReadOnlyDictionary<string, string> ParseHeaderLines(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = raw.IndexOf(':');
            if (index <= 0) continue;
            map[raw[..index].Trim()] = raw[(index + 1)..].Trim();
        }
        return map;
    }

    private static IReadOnlyDictionary<string, string> ParseQueryLines(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = raw.IndexOf('=');
            if (index <= 0) continue;
            map[raw[..index].Trim()] = raw[(index + 1)..].Trim();
        }
        return map;
    }

    private async Task InitializeRecordingUiAsync()
    {
        if (_services is null) return;
        UpdateRecordingControls(Services.Recording.State);
        RefreshRecordingAudioDevices();
        await RefreshRecordingCamerasAsync();
        try
        {
            var recovery = await Services.Recording.LoadRecoveryAsync();
            if (recovery.IsReadOnly)
            {
                RecordingStatusText.Text = recovery.Warning ?? "A newer recording recovery journal was left untouched.";
                RecordingDiscardRecoveryButton.Visibility = Visibility.Collapsed;
                return;
            }
            if (recovery.Manifest is { } manifest)
            {
                var partialExists = File.Exists(manifest.TemporaryPath);
                RecordingStatusText.Text = $"Unfinished recording detected from {manifest.UpdatedUtc.LocalDateTime:g}: {manifest.TargetSummary}. " +
                    (partialExists ? $"Partial output: {manifest.TemporaryPath}" : "The partial output is no longer present.");
                RecordingDiscardRecoveryButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            Services.Log.Error("RecordingRecoveryUi", ex);
            RecordingStatusText.Text = $"Recording recovery check failed: {ex.Message}";
        }
    }

    private void RecordingRefreshAudioDevices_Click(object sender, RoutedEventArgs e) => RefreshRecordingAudioDevices();

    private void RefreshRecordingAudioDevices()
    {
        if (_services is null) return;
        try
        {
            var selectedRenderId = (RecordingSystemAudioDeviceCombo.SelectedItem as RecordingAudioDevice)?.Id;
            var selectedCaptureId = (RecordingMicrophoneDeviceCombo.SelectedItem as RecordingAudioDevice)?.Id;
            var render = Services.RecordingAudioDevices.GetRenderDevices();
            var capture = Services.RecordingAudioDevices.GetCaptureDevices();
            RecordingSystemAudioDeviceCombo.ItemsSource = render;
            RecordingMicrophoneDeviceCombo.ItemsSource = capture;
            RecordingSystemAudioDeviceCombo.SelectedItem = render.FirstOrDefault(x => string.Equals(x.Id, selectedRenderId, StringComparison.OrdinalIgnoreCase))
                ?? render.FirstOrDefault(x => x.IsDefault) ?? render.FirstOrDefault();
            RecordingMicrophoneDeviceCombo.SelectedItem = capture.FirstOrDefault(x => string.Equals(x.Id, selectedCaptureId, StringComparison.OrdinalIgnoreCase))
                ?? capture.FirstOrDefault(x => x.IsDefault) ?? capture.FirstOrDefault();
            RecordingAudioStatusText.Text = $"Audio devices · {render.Count} output · {capture.Count} input";
        }
        catch (Exception ex)
        {
            Services.Log.Error("RecordingAudioDevices", ex);
            RecordingSystemAudioDeviceCombo.ItemsSource = null;
            RecordingMicrophoneDeviceCombo.ItemsSource = null;
            RecordingAudioStatusText.Text = $"Audio device enumeration failed: {ex.Message}";
        }
    }

    private async void RecordingRefreshCameras_Click(object sender, RoutedEventArgs e) => await RefreshRecordingCamerasAsync();

    private async Task RefreshRecordingCamerasAsync()
    {
        if (_services is null) return;
        try
        {
            var selectedId = (RecordingWebcamDeviceCombo.SelectedItem as CameraDeviceInfo)?.Id;
            var cameras = await CameraDeviceCatalog.ListAsync();
            RecordingWebcamDeviceCombo.ItemsSource = cameras;
            RecordingWebcamDeviceCombo.SelectedItem = cameras.FirstOrDefault(x => string.Equals(x.Id, selectedId, StringComparison.Ordinal))
                ?? cameras.FirstOrDefault();
            RecordingWebcamStatusText.Text = cameras.Count == 0
                ? "No camera detected. Webcam recording will fail closed if requested."
                : $"Cameras ready · {cameras.Count} device(s).";
        }
        catch (Exception ex)
        {
            Services.Log.Error("RecordingCameraDevices", ex);
            RecordingWebcamDeviceCombo.ItemsSource = null;
            RecordingWebcamStatusText.Text = $"Camera enumeration failed: {ex.Message}";
        }
    }

    private async void RecordingStart_Click(object sender, RoutedEventArgs e)
    {
        if (Services.Recording.IsActive)
        {
            ShowStatus("A recording is already active.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var options = ReadRecordingOptions();
            RecordingOutputPolicy.ValidateCompatibility(options);
            if (RecordingOutputPolicy.IsAudioOnly(options.OutputFormat))
            {
                await StartAudioOnlyRecordingAsync(options);
                return;
            }
            var target = await ResolveRecordingTargetAsync();
            if (target is null) return;
            await StartRecordingTargetAsync(target);
        }
        catch (Exception ex)
        {
            RestoreRecordingControlCaptureAffinity();
            Services.Log.Error("RecordingStart", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
            RecordingStatusText.Text = ex.Message;
        }
    }

    private async void RecordingRepeatRegion_Click(object sender, RoutedEventArgs e)
    {
        var last = Services.Recording.LastRegion;
        if (last is null)
        {
            ShowStatus("No recording region has been used in this app session yet.", InfoBarSeverity.Warning);
            return;
        }
        try { await StartRecordingTargetAsync(last); }
        catch (Exception ex)
        {
            RestoreRecordingControlCaptureAffinity();
            Services.Log.Error("RecordingRepeatRegion", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void RecordingPause_Click(object sender, RoutedEventArgs e)
    {
        try { await Services.Recording.PauseAsync(); }
        catch (Exception ex) { Services.Log.Error("RecordingPause", ex); ShowStatus(ex.Message, InfoBarSeverity.Warning); }
    }

    private async void RecordingResume_Click(object sender, RoutedEventArgs e)
    {
        try { await Services.Recording.ResumeAsync(); }
        catch (Exception ex) { Services.Log.Error("RecordingResume", ex); ShowStatus(ex.Message, InfoBarSeverity.Warning); }
    }

    private void RecordingStop_Click(object sender, RoutedEventArgs e)
    {
        Services.Recording.Stop();
        RecordingStatusText.Text = "Stopping and finalizing recording…";
        StopRecordingButton.IsEnabled = false;
    }

    private async void RecordingDiscardRecovery_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Services.Recording.ClearRecoveryAsync();
            RecordingDiscardRecoveryButton.Visibility = Visibility.Collapsed;
            RecordingStatusText.Text = "Recovery marker cleared. Any partial recording file was left untouched for manual inspection.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("RecordingRecoveryClear", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }


    private void OpenDocumentationBuilder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        var window = new DocumentationWindow(Services);
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private void OpenVideoEditor_Click(object sender, RoutedEventArgs e)
    {
        var window = new VideoEditorWindow(Services);
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private async Task StartAudioOnlyRecordingAsync(RecordingOptions options)
    {
        RecordingOutputPolicy.ValidateCompatibility(options);
        if (!RecordingOutputPolicy.IsAudioOnly(options.OutputFormat)) throw new InvalidOperationException("Audio-only start requires M4A output.");
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            SuggestedFileName = $"Magic-Capture-Audio-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add(RecordingOutputPolicy.DisplayName(options.OutputFormat), new List<string> { ".m4a" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (File.Exists(file.Path)) File.Delete(file.Path);
        RecordingDiscardRecoveryButton.Visibility = Visibility.Collapsed;
        RecordingStatusText.Text = options.CountdownSeconds > 0 ? $"Starting audio in {options.CountdownSeconds}…" : "Starting audio…";
        UpdateRecordingControls(RecordingSessionState.Preparing);
        await Services.Recording.StartAudioOnlyAsync(file.Path, options);
    }

    private async Task StartRecordingTargetAsync(RecordingTarget target)
    {
        var options = ReadRecordingOptions();
        RecordingOutputPolicy.ValidateCompatibility(options);
        var extension = RecordingOutputPolicy.Extension(options.OutputFormat);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = $"Magic-Capture-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add(RecordingOutputPolicy.DisplayName(options.OutputFormat), new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        // The picker reserves/creates the target file. Recording writes to a same-directory partial
        // output and promotes it only after the selected encoder finishes successfully.
        if (File.Exists(file.Path)) File.Delete(file.Path);

        var hwnd = WindowHelpers.GetWindowHandle(this);
        RecordingControlCaptureExclusion.Exclude(hwnd);
        _recordingCaptureExclusionApplied = true;
        RecordingDiscardRecoveryButton.Visibility = Visibility.Collapsed;
        RecordingStatusText.Text = options.CountdownSeconds > 0 ? $"Starting in {options.CountdownSeconds}…" : "Starting…";
        UpdateRecordingControls(RecordingSessionState.Preparing);
        await Services.Recording.StartAsync(target, file.Path, options);
    }

    private async Task<RecordingTarget?> ResolveRecordingTargetAsync()
    {
        var tag = (RecordingTargetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Region";
        if (!Enum.TryParse<RecordingTargetKind>(tag, out var kind)) kind = RecordingTargetKind.Region;

        switch (kind)
        {
            case RecordingTargetKind.Region:
            {
                var appWindow = WindowHelpers.GetAppWindow(this);
                appWindow.Hide();
                CaptureRequestResult? selection;
                try
                {
                    selection = await Services.Capture.CaptureRegionAsync(
                        defaultAction: OverlayCaptureAction.Result,
                        includeCursor: false,
                        tier: Services.Entitlements.Current.Tier,
                        overlayTheme: Services.Settings.CaptureOverlayTheme,
                        rectangularOnly: true,
                        actionLayout: Services.Settings.OverlayActions);
                }
                finally
                {
                    appWindow.Show();
                    Activate();
                }
                return selection is null
                    ? null
                    : new RecordingTarget(RecordingTargetKind.Region, selection.SelectionBounds,
                        $"Region {selection.SelectionBounds.Width}×{selection.SelectionBounds.Height} @ {selection.SelectionBounds.X},{selection.SelectionBounds.Y}");
            }
            case RecordingTargetKind.Window:
            {
                var windows = Services.WindowCapture.ListCapturableWindows();
                if (windows.Count == 0) throw new InvalidOperationException("No capturable desktop windows were found.");
                var list = new ListView { ItemsSource = windows, SelectionMode = ListViewSelectionMode.Single, MinWidth = 640, MaxHeight = 420, SelectedIndex = 0 };
                var dialog = new ContentDialog { Title = "Choose window to record", Content = list, PrimaryButtonText = "Record window", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not WindowCaptureTarget window) return null;
                return new RecordingTarget(RecordingTargetKind.Window, window.Bounds, window.DisplayName, windowHandle: window.Handle);
            }
            case RecordingTargetKind.Monitor:
            {
                var monitors = Services.Monitors.ListMonitors();
                if (monitors.Count == 0) throw new InvalidOperationException("No monitor is currently available.");
                var list = new ListView { ItemsSource = monitors, SelectionMode = ListViewSelectionMode.Single, MinWidth = 560, MaxHeight = 360, SelectedIndex = 0 };
                var dialog = new ContentDialog { Title = "Choose monitor to record", Content = list, PrimaryButtonText = "Record monitor", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not MonitorInfo monitor) return null;
                return new RecordingTarget(RecordingTargetKind.Monitor, monitor.Bounds, monitor.DisplayName, monitorHandle: monitor.Handle, monitorName: monitor.DeviceName);
            }
            case RecordingTargetKind.VirtualDesktop:
            {
                var bounds = Services.Monitors.GetVirtualScreenBounds();
                return new RecordingTarget(RecordingTargetKind.VirtualDesktop, bounds,
                    $"Virtual desktop {bounds.Width}×{bounds.Height} @ {bounds.X},{bounds.Y}");
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private void RecordingOutputFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_services is null) return;
        var format = SelectedRecordingOutputFormat();
        if (!RecordingEffectsPolicy.SupportsAudio(format))
        {
            RecordingSystemAudioCheck.IsChecked = false;
            RecordingMicrophoneCheck.IsChecked = false;
            RecordingAudioStatusText.Text = $"{RecordingOutputPolicy.DisplayName(format)} is visual-only; audio is disabled.";
        }
        else if (RecordingOutputPolicy.IsAudioOnly(format))
        {
            RecordingCursorCheck.IsChecked = false;
            RecordingWebcamCheck.IsChecked = false;
            RecordingCursorHighlightCheck.IsChecked = false;
            RecordingClickVisualizationCheck.IsChecked = false;
            RecordingSafeKeyOverlayCheck.IsChecked = false;
            RecordingDrawCheck.IsChecked = false;
            RecordingLiveZoomCheck.IsChecked = false;
            if (RecordingSystemAudioCheck.IsChecked != true && RecordingMicrophoneCheck.IsChecked != true)
                RecordingMicrophoneCheck.IsChecked = true;
            RecordingAudioStatusText.Text = "Audio-only mode · choose system audio, microphone, or both.";
        }
        if (_services is not null) UpdateRecordingControls(Services.Recording.State);
    }

    private RecordingOutputFormat SelectedRecordingOutputFormat()
    {
        var tag = (RecordingOutputFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? nameof(RecordingOutputFormat.Mp4);
        return Enum.TryParse<RecordingOutputFormat>(tag, out var format) ? format : RecordingOutputFormat.Mp4;
    }

    private RecordingOptions ReadRecordingOptions()
    {
        var stop = BoundRecordingNumber(RecordingStopAfterBox.Value, 0, RecordingRules.MaximumStopAfterMinutes, 0);
        var customX = BoundRecordingNumber(RecordingWebcamXBox.Value, 0, 100, 100);
        var customY = BoundRecordingNumber(RecordingWebcamYBox.Value, 0, 100, 100);
        var position = (RecordingWebcamPositionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "BottomRight";
        var (webcamX, webcamY) = position switch
        {
            "TopLeft" => (0, 0),
            "TopRight" => (100, 0),
            "BottomLeft" => (0, 100),
            "BottomRight" => (100, 100),
            _ => (customX, customY)
        };
        var shapeTag = (RecordingWebcamShapeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? nameof(WebcamOverlayShape.Rounded);
        if (!Enum.TryParse<WebcamOverlayShape>(shapeTag, out var webcamShape)) webcamShape = WebcamOverlayShape.Rounded;
        var outputTag = (RecordingOutputFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? nameof(RecordingOutputFormat.Mp4);
        if (!Enum.TryParse<RecordingOutputFormat>(outputTag, out var outputFormat)) outputFormat = RecordingOutputFormat.Mp4;
        return RecordingRules.Normalize(new RecordingOptions(
            FramesPerSecond: BoundRecordingNumber(RecordingFpsBox.Value, RecordingRules.MinimumFramesPerSecond, RecordingRules.MaximumFramesPerSecond, 30),
            BitrateMbps: BoundRecordingNumber(RecordingBitrateBox.Value, RecordingRules.MinimumBitrateMbps, RecordingRules.MaximumBitrateMbps, 8),
            ScalePercent: BoundRecordingNumber(RecordingScaleBox.Value, RecordingRules.MinimumScalePercent, RecordingRules.MaximumScalePercent, 100),
            IncludeCursor: RecordingCursorCheck.IsChecked == true,
            CountdownSeconds: BoundRecordingNumber(RecordingCountdownBox.Value, RecordingRules.MinimumCountdownSeconds, RecordingRules.MaximumCountdownSeconds, 3),
            StopAfterMinutes: stop <= 0 ? null : stop,
            IncludeSystemAudio: RecordingSystemAudioCheck.IsChecked == true,
            IncludeMicrophone: RecordingMicrophoneCheck.IsChecked == true,
            SystemAudioDeviceId: (RecordingSystemAudioDeviceCombo.SelectedItem as RecordingAudioDevice)?.Id,
            MicrophoneDeviceId: (RecordingMicrophoneDeviceCombo.SelectedItem as RecordingAudioDevice)?.Id,
            AudioBitrateKbps: BoundRecordingNumber(RecordingAudioBitrateBox.Value, RecordingRules.MinimumAudioBitrateKbps, RecordingRules.MaximumAudioBitrateKbps, 192),
            SystemAudioGainPercent: BoundRecordingNumber(RecordingSystemAudioGainBox.Value, RecordingRules.MinimumAudioGainPercent, RecordingRules.MaximumAudioGainPercent, 100),
            MicrophoneGainPercent: BoundRecordingNumber(RecordingMicrophoneGainBox.Value, RecordingRules.MinimumAudioGainPercent, RecordingRules.MaximumAudioGainPercent, 100),
            IncludeWebcam: RecordingWebcamCheck.IsChecked == true,
            WebcamDeviceId: (RecordingWebcamDeviceCombo.SelectedItem as CameraDeviceInfo)?.Id,
            WebcamXPercent: webcamX,
            WebcamYPercent: webcamY,
            WebcamWidthPercent: BoundRecordingNumber(RecordingWebcamWidthBox.Value, RecordingWebcamPolicy.MinimumWidthPercent, RecordingWebcamPolicy.MaximumWidthPercent, 25),
            WebcamShape: webcamShape,
            MirrorWebcam: RecordingWebcamMirrorCheck.IsChecked == true,
            WebcamOpacityPercent: BoundRecordingNumber(RecordingWebcamOpacityBox.Value, RecordingWebcamPolicy.MinimumOpacityPercent, RecordingWebcamPolicy.MaximumOpacityPercent, 100),
            WebcamBorderPixels: BoundRecordingNumber(RecordingWebcamBorderBox.Value, 0, RecordingWebcamPolicy.MaximumBorderPixels, 2),
            OutputFormat: outputFormat,
            CursorHighlight: RecordingCursorHighlightCheck.IsChecked == true,
            ClickVisualization: RecordingClickVisualizationCheck.IsChecked == true,
            SafeKeyOverlay: RecordingSafeKeyOverlayCheck.IsChecked == true,
            DrawWhileRecording: RecordingDrawCheck.IsChecked == true,
            LiveZoom: RecordingLiveZoomCheck.IsChecked == true,
            ZoomPercent: BoundRecordingNumber(RecordingZoomBox.Value, RecordingEffectsPolicy.MinimumZoomPercent, RecordingEffectsPolicy.MaximumZoomPercent, RecordingEffectsPolicy.DefaultZoomPercent)));
    }

    private static int BoundRecordingNumber(double value, int minimum, int maximum, int fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return Math.Clamp(fallback, minimum, maximum);
        return Math.Clamp(checked((int)Math.Round(value)), minimum, maximum);
    }

    private void Recording_ProgressChanged(object? sender, RecordingProgress progress)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RecordingElapsedText.Text = progress.ActiveElapsed.ToString(@"hh\:mm\:ss");
            RecordingStatusText.Text = progress.Message ?? progress.State switch
            {
                RecordingSessionState.Preparing when progress.CountdownRemaining > 0 => $"Starting in {progress.CountdownRemaining}… · {progress.Target}",
                RecordingSessionState.Preparing => $"Preparing · {progress.Target}",
                RecordingSessionState.Recording => $"Recording · {progress.FrameCount:N0} frame(s) · {progress.Target}",
                RecordingSessionState.Paused => $"Paused · {progress.FrameCount:N0} frame(s) · {progress.Target}",
                RecordingSessionState.Finalizing => $"Finalizing recording · {progress.FrameCount:N0} frame(s)…",
                RecordingSessionState.Completed => string.IsNullOrWhiteSpace(progress.OutputPath) ? "Recording completed." : $"Saved · {progress.OutputPath}",
                RecordingSessionState.Failed => "Recording failed.",
                _ => progress.State.ToString()
            };
            if (progress.AudioStatus is { } audio)
            {
                var systemPeak = Math.Clamp(audio.SystemLevel.Peak * 100, 0, 100);
                var microphonePeak = Math.Clamp(audio.MicrophoneLevel.Peak * 100, 0, 100);
                RecordingAudioStatusText.Text = $"System: {audio.SystemSource} · {systemPeak:F0}% peak  |  Mic: {audio.MicrophoneSource} · {microphonePeak:F0}% peak  |  dropped: {audio.DroppedBytes:N0} B";
            }
            else if (progress.State is RecordingSessionState.Completed or RecordingSessionState.Failed)
            {
                RecordingAudioStatusText.Text = "Audio off.";
            }
            if (progress.WebcamStatus is { } webcam)
            {
                RecordingWebcamStatusText.Text = webcam.Failure is { Length: > 0 } failure
                    ? $"Webcam failed: {failure}"
                    : webcam.Active
                        ? $"Webcam active · {webcam.Width}×{webcam.Height}"
                        : "Webcam preparing…";
            }
            else if (progress.State is RecordingSessionState.Completed or RecordingSessionState.Failed)
            {
                RecordingWebcamStatusText.Text = "Webcam off.";
            }
            UpdateRecordingControls(progress.State);
            if (progress.State is RecordingSessionState.Completed or RecordingSessionState.Failed)
                RestoreRecordingControlCaptureAffinity();
        });
    }

    private void UpdateRecordingControls(RecordingSessionState state)
    {
        var active = state is RecordingSessionState.Preparing or RecordingSessionState.Recording or RecordingSessionState.Paused or RecordingSessionState.Finalizing;
        StartRecordingButton.IsEnabled = !active;
        var outputFormat = SelectedRecordingOutputFormat();
        var audioOnly = RecordingOutputPolicy.IsAudioOnly(outputFormat);
        RepeatRecordingRegionButton.IsEnabled = !active && !audioOnly;
        RecordingTargetCombo.IsEnabled = !active && !audioOnly;
        var audioAllowed = RecordingEffectsPolicy.SupportsAudio(outputFormat);
        RecordingFpsBox.IsEnabled = !active && !audioOnly;
        RecordingBitrateBox.IsEnabled = !active && outputFormat == RecordingOutputFormat.Mp4;
        RecordingScaleBox.IsEnabled = !active && !audioOnly;
        RecordingOutputFormatCombo.IsEnabled = !active;
        RecordingCountdownBox.IsEnabled = !active;
        RecordingStopAfterBox.IsEnabled = !active;
        RecordingCursorCheck.IsEnabled = !active && !audioOnly;
        RecordingCursorHighlightCheck.IsEnabled = !active && !audioOnly;
        RecordingClickVisualizationCheck.IsEnabled = !active && !audioOnly;
        RecordingSafeKeyOverlayCheck.IsEnabled = !active && !audioOnly;
        RecordingDrawCheck.IsEnabled = !active && !audioOnly;
        RecordingLiveZoomCheck.IsEnabled = !active && !audioOnly;
        RecordingZoomBox.IsEnabled = !active && !audioOnly;
        RecordingSystemAudioCheck.IsEnabled = !active && audioAllowed;
        RecordingMicrophoneCheck.IsEnabled = !active && audioAllowed;
        RecordingSystemAudioDeviceCombo.IsEnabled = !active && audioAllowed;
        RecordingMicrophoneDeviceCombo.IsEnabled = !active && audioAllowed;
        RecordingSystemAudioGainBox.IsEnabled = !active && audioAllowed;
        RecordingMicrophoneGainBox.IsEnabled = !active && audioAllowed;
        RecordingAudioBitrateBox.IsEnabled = !active && audioAllowed;
        RecordingWebcamCheck.IsEnabled = !active && !audioOnly;
        RecordingWebcamDeviceCombo.IsEnabled = !active && !audioOnly;
        RecordingWebcamMirrorCheck.IsEnabled = !active && !audioOnly;
        RecordingWebcamPositionCombo.IsEnabled = !active && !audioOnly;
        RecordingWebcamXBox.IsEnabled = !active && !audioOnly;
        RecordingWebcamYBox.IsEnabled = !active && !audioOnly;
        RecordingWebcamWidthBox.IsEnabled = !active && !audioOnly;
        RecordingWebcamShapeCombo.IsEnabled = !active && !audioOnly;
        RecordingWebcamOpacityBox.IsEnabled = !active && !audioOnly;
        RecordingWebcamBorderBox.IsEnabled = !active && !audioOnly;
        PauseRecordingButton.IsEnabled = state == RecordingSessionState.Recording;
        ResumeRecordingButton.IsEnabled = state == RecordingSessionState.Paused;
        StopRecordingButton.IsEnabled = state is RecordingSessionState.Preparing or RecordingSessionState.Recording or RecordingSessionState.Paused;
    }

    private void RestoreRecordingControlCaptureAffinity()
    {
        if (!_recordingCaptureExclusionApplied) return;
        try { RecordingControlCaptureExclusion.Restore(WindowHelpers.GetWindowHandle(this)); }
        catch (Exception ex) { if (_services is not null) Services.Log.Error("RecordingCaptureAffinityRestore", ex); }
        finally { _recordingCaptureExclusionApplied = false; }
    }

    private int SelectedDelaySeconds() => int.TryParse((CaptureDelayCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var seconds) ? seconds : 0;

    private async void CaptureRegion_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).CaptureRegionFromUiAsync(SelectedDelaySeconds());
    private async void RepeatRegion_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).CaptureRepeatRegionAsync();
    private async void CaptureMonitor_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).CaptureActiveMonitorAsync(SelectedDelaySeconds());
    private async void CaptureVirtual_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).CaptureVirtualDesktopAsync(SelectedDelaySeconds());
    private async void CaptureWindow_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).CaptureForegroundWindowAsync(SelectedDelaySeconds());
    private async void CaptureAutoScroll_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).CaptureAutomaticScrollAsync();

    private async void CaptureMonitorMenu_Click(object sender, RoutedEventArgs e)
    {
        var monitors = Services.Monitors.ListMonitors();
        if (monitors.Count == 0) { ShowStatus("No monitor is currently available.", InfoBarSeverity.Warning); return; }
        var list = new ListView { ItemsSource = monitors, SelectionMode = ListViewSelectionMode.Single, MinWidth = 520, MaxHeight = 360, SelectedIndex = 0 };
        var dialog = new ContentDialog { Title = "Choose monitor", Content = list, PrimaryButtonText = "Capture", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not MonitorInfo monitor) return;
        await ((App)Application.Current).CaptureMonitorTargetAsync(monitor, SelectedDelaySeconds());
    }

    private async void CaptureWindowMenu_Click(object sender, RoutedEventArgs e)
    {
        var windows = Services.WindowCapture.ListCapturableWindows();
        if (windows.Count == 0) { ShowStatus("No capturable desktop windows were found.", InfoBarSeverity.Warning); return; }
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Select one or more windows. Up to 16 are captured from this snapshot list.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        var list = new ListView { ItemsSource = windows, SelectionMode = ListViewSelectionMode.Multiple, MinWidth = 640, MaxHeight = 420 };
        panel.Children.Add(list);
        var dialog = new ContentDialog { Title = "Choose window(s)", Content = panel, PrimaryButtonText = "Capture selected", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var targets = list.SelectedItems.OfType<WindowCaptureTarget>().Take(16).ToArray();
        if (targets.Length == 0) { ShowStatus("Select at least one window.", InfoBarSeverity.Warning); return; }
        await ((App)Application.Current).CaptureWindowTargetsAsync(targets, SelectedDelaySeconds());
    }

    private async void RunCaptureProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureProfileCombo.SelectedItem is not CaptureProfile profile)
        {
            ShowStatus("Choose a saved capture profile first.", InfoBarSeverity.Warning);
            return;
        }
        await ((App)Application.Current).RunCaptureProfileAsync(profile);
    }

    private async void CaptureRecentRegion_Click(object sender, RoutedEventArgs e)
    {
        if (RecentRegionCombo.SelectedItem is not RecentRegionOption recent)
        {
            ShowStatus("No recent region is available yet.", InfoBarSeverity.Warning);
            return;
        }
        var profile = new CaptureProfile(Guid.NewGuid().ToString("N"), "Recent region", CaptureProfileSource.Region,
            recent.Bounds, Services.Settings.CaptureCursor, SelectedDelaySeconds() * 1000, Services.Settings.DefaultPostCaptureAction);
        await ((App)Application.Current).RunCaptureProfileAsync(profile);
    }

    private async void CaptureExactRegion_Click(object sender, RoutedEventArgs e)
    {
        var recent = Services.Settings.RecentRegions.FirstOrDefault();
        var initial = recent.IsEmpty ? Services.Monitors.GetVirtualScreenBounds() : recent;
        var region = await ShowExactRegionDialogAsync("Capture exact region", initial);
        if (region is null) return;
        var profile = new CaptureProfile(Guid.NewGuid().ToString("N"), "Exact region", CaptureProfileSource.Region,
            region, Services.Settings.CaptureCursor, SelectedDelaySeconds() * 1000, Services.Settings.DefaultPostCaptureAction);
        await ((App)Application.Current).RunCaptureProfileAsync(profile);
    }

    private async void NewCaptureProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Profile name", Text = "My capture" };
        var source = new ComboBox { Header = "Capture source", Width = 260, ItemsSource = new[] { "Region", "Foreground window", "Active monitor", "Virtual desktop" }, SelectedIndex = 0 };
        var recent = Services.Settings.RecentRegions.FirstOrDefault();
        var initial = recent.IsEmpty ? Services.Monitors.GetVirtualScreenBounds() : recent;
        var x = new NumberBox { Header = "X", Value = initial.X, Minimum = -100000, Maximum = 100000, Width = 120 };
        var y = new NumberBox { Header = "Y", Value = initial.Y, Minimum = -100000, Maximum = 100000, Width = 120 };
        var width = new NumberBox { Header = "Width", Value = Math.Max(1, initial.Width), Minimum = 1, Maximum = 100000, Width = 140 };
        var height = new NumberBox { Header = "Height", Value = Math.Max(1, initial.Height), Minimum = 1, Maximum = 100000, Width = 140 };
        var regionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        regionPanel.Children.Add(x); regionPanel.Children.Add(y); regionPanel.Children.Add(width); regionPanel.Children.Add(height);
        var cursor = new CheckBox { Content = "Include cursor", IsChecked = Services.Settings.CaptureCursor };
        var delay = new NumberBox { Header = "Delay (seconds)", Value = 0, Minimum = 0, Maximum = 60, Width = 160 };
        var action = new ComboBox { Header = "After capture", Width = 220, ItemsSource = Enum.GetValues<PostCaptureAction>(), SelectedItem = Services.Settings.DefaultPostCaptureAction };
        var format = new ComboBox { Header = "Save format", Width = 180, ItemsSource = new[] { "png", "jpeg", "bmp", "tiff", "pdf" }, SelectedIndex = 0 };
        var workflowOptions = new List<CaptureProfileWorkflowOption> { new("None", null) };
        workflowOptions.AddRange(_workflowItems.Select(item => new CaptureProfileWorkflowOption(item.Name, item.Id)));
        var workflow = new ComboBox { Header = "Workflow (optional)", Width = 320, ItemsSource = workflowOptions, SelectedIndex = 0 };
        source.SelectionChanged += (_, _) => regionPanel.Visibility = source.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;

        var panel = new StackPanel { Spacing = 10, MinWidth = 560 };
        panel.Children.Add(name); panel.Children.Add(source); panel.Children.Add(regionPanel); panel.Children.Add(cursor); panel.Children.Add(delay); panel.Children.Add(action); panel.Children.Add(format); panel.Children.Add(workflow);
        var dialog = new ContentDialog
        {
            Title = "New capture profile",
            Content = panel,
            PrimaryButtonText = "Save profile",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text)) { ShowStatus("Profile name is required.", InfoBarSeverity.Warning); return; }

        var profileSource = source.SelectedIndex switch
        {
            1 => CaptureProfileSource.ForegroundWindow,
            2 => CaptureProfileSource.ActiveMonitor,
            3 => CaptureProfileSource.VirtualDesktop,
            _ => CaptureProfileSource.Region
        };
        PixelRect? bounds = null;
        if (profileSource == CaptureProfileSource.Region)
        {
            var requested = new PixelRect((int)x.Value, (int)y.Value, (int)width.Value, (int)height.Value);
            var normalized = CaptureRegionRules.Normalize(requested, Services.Monitors.GetVirtualScreenBounds());
            if (normalized.IsEmpty) { ShowStatus("The exact region is outside the current desktop.", InfoBarSeverity.Warning); return; }
            bounds = normalized;
        }
        var profile = new CaptureProfile(
            Guid.NewGuid().ToString("N"),
            name.Text.Trim(),
            profileSource,
            bounds,
            cursor.IsChecked == true,
            (int)Math.Round(delay.Value * 1000),
            action.SelectedItem is PostCaptureAction selectedAction ? selectedAction : PostCaptureAction.ResultWindow,
            (workflow.SelectedItem as CaptureProfileWorkflowOption)?.WorkflowId,
            format.SelectedItem?.ToString() ?? "png").Normalize();
        var saved = await ((App)Application.Current).TryMutateSettingsAsync(
            current => current with
            {
                CaptureProfiles = current.CaptureProfiles.Where(item => item.Id != profile.Id).Append(profile).ToArray(),
                DefaultCaptureProfileId = profile.Id
            },
            SettingsRuntimeEffects.MainWindowUi,
            logComponent: "CaptureProfileSave");
        if (!saved) return;
        CaptureProfileCombo.SelectedItem = Services.Settings.CaptureProfiles.FirstOrDefault(item => item.Id == profile.Id);
        ShowStatus($"Saved capture profile ‘{profile.Name}’.", InfoBarSeverity.Success);
    }

    private async void DeleteCaptureProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureProfileCombo.SelectedItem is not CaptureProfile profile) return;
        var dialog = new ContentDialog
        {
            Title = "Delete capture profile?",
            Content = $"Delete ‘{profile.Name}’? This does not delete any captures.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var triggerDependents = WorkflowReferencePolicy.FindCaptureProfileDependents(profile.Id, await Services.WorkflowTriggers.LoadAsync());
        if (triggerDependents.Count > 0)
        {
            ShowStatus("Capture profile is still used by " + string.Join(", ", triggerDependents) + ". Rebind or disable those triggers first.", InfoBarSeverity.Warning);
            return;
        }
        _ = await ((App)Application.Current).TryMutateSettingsAsync(
            current => SettingsReferencePolicy.RemoveCaptureProfileReferences(current, profile.Id),
            SettingsRuntimeEffects.MainWindowUi,
            logComponent: "CaptureProfileDelete");
    }

    private async Task<PixelRect?> ShowExactRegionDialogAsync(string title, PixelRect initial)
    {
        var x = new NumberBox { Header = "X", Value = initial.X, Minimum = -100000, Maximum = 100000, Width = 120 };
        var y = new NumberBox { Header = "Y", Value = initial.Y, Minimum = -100000, Maximum = 100000, Width = 120 };
        var width = new NumberBox { Header = "Width", Value = Math.Max(1, initial.Width), Minimum = 1, Maximum = 100000, Width = 140 };
        var height = new NumberBox { Header = "Height", Value = Math.Max(1, initial.Height), Minimum = 1, Maximum = 100000, Width = 140 };
        var preset = new ComboBox { Header = "Preset size", MinWidth = 250 };
        preset.Items.Add("Custom");
        foreach (var item in CaptureSizePresets.BuiltIn) preset.Items.Add(item);
        preset.SelectedIndex = 0;
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not CaptureSizePreset selected) return;
            width.Value = selected.Width;
            height.Value = selected.Height;
        };
        width.ValueChanged += (_, _) => { if (preset.SelectedItem is CaptureSizePreset selected && (Math.Round(width.Value) != selected.Width || Math.Round(height.Value) != selected.Height)) preset.SelectedIndex = 0; };
        height.ValueChanged += (_, _) => { if (preset.SelectedItem is CaptureSizePreset selected && (Math.Round(width.Value) != selected.Width || Math.Round(height.Value) != selected.Height)) preset.SelectedIndex = 0; };

        var coordinates = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        coordinates.Children.Add(x); coordinates.Children.Add(y); coordinates.Children.Add(width); coordinates.Children.Add(height);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(preset);
        panel.Children.Add(coordinates);
        var dialog = new ContentDialog { Title = title, Content = panel, PrimaryButtonText = "Capture", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var requested = new PixelRect((int)x.Value, (int)y.Value, (int)width.Value, (int)height.Value);
        var normalized = CaptureRegionRules.Normalize(requested, Services.Monitors.GetVirtualScreenBounds());
        if (normalized.IsEmpty)
        {
            ShowStatus("The requested region is outside the current desktop.", InfoBarSeverity.Warning);
            return null;
        }
        return normalized;
    }


    private async Task RecordHistoryWorkflowStartBestEffortAsync(IEnumerable<Guid> assetIds, CaptureWorkflow workflow, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var assetId in assetIds.Where(id => id != Guid.Empty).Distinct().Take(WorkflowRuntimePolicy.MaximumBatchAssets))
                await Services.HistoryLibrary.RecordWorkflowAsync(assetId, workflow.Id, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { Services.Log.Error("HistoryWorkflowActivity", ex); }
    }

    private async Task RecordAiActionsBestEffortAsync(IEnumerable<Guid> assetIds, CaptureWorkflow workflow, WorkflowExecutionResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            var attemptedStepIds = result.Steps
                .Where(step => step.Status is not WorkflowStepStatus.Skipped and not WorkflowStepStatus.WouldRun)
                .Select(step => step.StepId)
                .ToHashSet(StringComparer.Ordinal);
            var actionIds = workflow.Steps
                .Where(step => step.IsEnabled != false && step.Kind == WorkflowStepKind.RunMagicAction && attemptedStepIds.Contains(step.Id) && !string.IsNullOrWhiteSpace(step.Argument))
                .Select(step => step.Argument!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var assetId in assetIds.Where(id => id != Guid.Empty).Distinct().Take(WorkflowRuntimePolicy.MaximumBatchAssets))
                foreach (var actionId in actionIds)
                    await Services.HistoryLibrary.RecordAiActionAsync(assetId, actionId, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { Services.Log.Error("HistoryAiActionActivity", ex); }
    }

    internal void RefreshHistorySoon() => _ = RefreshHistoryAsync();

    private async Task RefreshHistoryAsync()
    {
        if (_services is null) return;

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _historyRefreshCts, cts);
        previous?.Cancel();
        var generation = Interlocked.Increment(ref _historyRefreshGeneration);
        try
        {
            var items = await Services.HistoryStore.ListAsync(cts.Token);
            var library = await Services.HistoryLibrary.LoadAsync(cts.Token);
            var displays = new List<HistoryDisplayItem>(items.Count);
            foreach (var item in items)
            {
                cts.Token.ThrowIfCancellationRequested();
                var thumbnail = Services.HistoryStore.GetThumbnailAbsolutePath(item);
                var imagePath = !string.IsNullOrWhiteSpace(thumbnail) && File.Exists(thumbnail)
                    ? thumbnail
                    : Services.HistoryStore.GetAbsolutePath(item);
                if (!File.Exists(imagePath)) continue;
                try
                {
                    displays.Add(await HistoryDisplayItem.CreateAsync(item, imagePath, Services.HistoryProcessIcons));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Services.Log.Error("HistoryThumbnail", ex);
                }
            }

            cts.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _historyRefreshGeneration)) return;
            _historyDisplayItems = displays;
            _historyLibrarySnapshot = library;
            RefreshHistoryOrganizationSelectors();
            await ApplyHistoryFilterAsync();
            RecentHistoryList.ItemsSource = displays.Take(8).ToArray();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A newer refresh owns the UI state; the older result must not overwrite it.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Log.Error("HistoryRefresh", ex);
            if (generation == Volatile.Read(ref _historyRefreshGeneration))
                ShowStatus("History could not be refreshed. Keeping the previous list.", InfoBarSeverity.Warning);
        }
        finally
        {
            Interlocked.CompareExchange(ref _historyRefreshCts, null, cts);
            cts.Dispose();
        }
    }


    private async void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try { await ApplyHistoryFilterAsync(150); }
        catch (OperationCanceledException) { }
    }

    private async Task ApplyHistoryFilterAsync(int debounceMilliseconds = 0)
    {
        if (HistoryList is null || HistorySearchBox is null || _services is null) return;
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _historySearchCts, cts);
        previous?.Cancel();
        var generation = Interlocked.Increment(ref _historySearchGeneration);
        try
        {
            if (debounceMilliseconds > 0) await Task.Delay(debounceMilliseconds, cts.Token);
            var queryText = HistorySearchBox.Text;
            var matches = await Services.HistoryStore.SearchAsync(queryText, _historyQueryOptions, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _historySearchGeneration)) return;

            var displayById = _historyDisplayItems.ToDictionary(display => display.Item.Id);
            var filtered = matches.Where(item => displayById.ContainsKey(item.Id)).Select(item => displayById[item.Id]).ToArray();
            ApplyHistoryView(filtered);
            HistorySearchCountText.Text = string.IsNullOrWhiteSpace(queryText) && CountActiveHistoryFilters() == 0
                ? $"{_historyDisplayItems.Count} capture(s)"
                : $"{filtered.Length} of {_historyDisplayItems.Count} capture(s)";
            HistoryFilterStatusText.Text = CountActiveHistoryFilters() is var count && count > 0 ? $"{count} filter(s) active" : string.Empty;
            if (_historyQueryOptions.Sort == HistorySortOrder.MostUsed) HistoryFilterStatusText.Text += " · Most used";
        }
        finally
        {
            Interlocked.CompareExchange(ref _historySearchCts, null, cts);
            cts.Dispose();
        }
    }

    private int CountActiveHistoryFilters()
    {
        var options = _historyQueryOptions;
        var count = 0;
        if (options.FromUtc is not null) count++;
        if (options.ToUtc is not null) count++;
        if (!string.IsNullOrWhiteSpace(options.SourceOrAppContains)) count++;
        if (!string.IsNullOrWhiteSpace(options.WindowContains)) count++;
        if (!string.IsNullOrWhiteSpace(options.MonitorContains)) count++;
        if (!string.IsNullOrWhiteSpace(options.CaptureType)) count++;
        if (options.MinWidth is not null || options.MaxWidth is not null || options.MinHeight is not null || options.MaxHeight is not null) count++;
        if (options.HasOcr is not null) count++;
        if (options.HasBarcode is not null) count++;
        if (options.IsFavorite is not null) count++;
        if (!string.IsNullOrWhiteSpace(options.SessionId)) count++;
        if (!string.IsNullOrWhiteSpace(options.WorkspaceId)) count++;
        if (!string.IsNullOrWhiteSpace(options.FolderId)) count++;
        if (!string.IsNullOrWhiteSpace(options.CollectionId)) count++;
        if (!string.IsNullOrWhiteSpace(options.WorkflowId)) count++;
        if (!string.IsNullOrWhiteSpace(options.AiActionId)) count++;
        if (options.Sort != HistorySortOrder.Newest) count++;
        return count;
    }

    private async void HistoryFilters_Click(object sender, RoutedEventArgs e)
    {
        var source = new TextBox { Header = "Source / app", Text = _historyQueryOptions.SourceOrAppContains ?? string.Empty, PlaceholderText = "Chrome, Monitor, Window…" };
        var window = new TextBox { Header = "Window title", Text = _historyQueryOptions.WindowContains ?? string.Empty };
        var monitor = new TextBox { Header = "Monitor", Text = _historyQueryOptions.MonitorContains ?? string.Empty, PlaceholderText = "DISPLAY1" };
        var type = new ComboBox { Header = "Capture type", MinWidth = 180 };
        type.Items.Add("Any");
        foreach (var value in _historyDisplayItems.Select(item => item.Item.SourceKind).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value)) type.Items.Add(value);
        type.SelectedItem = string.IsNullOrWhiteSpace(_historyQueryOptions.CaptureType) ? "Any" : _historyQueryOptions.CaptureType;
        var from = new CalendarDatePicker { Header = "From date", Date = _historyQueryOptions.FromUtc };
        var to = new CalendarDatePicker { Header = "To date", Date = _historyQueryOptions.ToUtc };
        var minWidth = new NumberBox { Header = "Min width", Minimum = 0, Maximum = 100000, Value = _historyQueryOptions.MinWidth ?? 0 };
        var maxWidth = new NumberBox { Header = "Max width", Minimum = 0, Maximum = 100000, Value = _historyQueryOptions.MaxWidth ?? 0 };
        var minHeight = new NumberBox { Header = "Min height", Minimum = 0, Maximum = 100000, Value = _historyQueryOptions.MinHeight ?? 0 };
        var maxHeight = new NumberBox { Header = "Max height", Minimum = 0, Maximum = 100000, Value = _historyQueryOptions.MaxHeight ?? 0 };
        var ocr = CreateAnyYesNoCombo("OCR text", _historyQueryOptions.HasOcr);
        var barcode = CreateAnyYesNoCombo("Barcode / QR", _historyQueryOptions.HasBarcode);
        var favorite = CreateAnyYesNoCombo("Favorite", _historyQueryOptions.IsFavorite);
        var session = new TextBox { Header = "Session ID", Text = _historyQueryOptions.SessionId ?? string.Empty };
        var workflow = CreateAnyStringCombo("Workflow", _historyLibrarySnapshot.Assets.SelectMany(item => item.WorkflowIds ?? []), _historyQueryOptions.WorkflowId);
        var aiAction = CreateAnyStringCombo("AI action", _historyLibrarySnapshot.Assets.SelectMany(item => item.AiActionIds ?? []), _historyQueryOptions.AiActionId);
        var sort = new ComboBox { Header = "Sort", MinWidth = 200, ItemsSource = Enum.GetValues<HistorySortOrder>(), SelectedItem = _historyQueryOptions.Sort };
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 9; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHistoryFilterControl(grid, source, 0, 0); AddHistoryFilterControl(grid, window, 0, 1);
        AddHistoryFilterControl(grid, monitor, 1, 0); AddHistoryFilterControl(grid, type, 1, 1);
        AddHistoryFilterControl(grid, sort, 2, 0); AddHistoryFilterControl(grid, session, 2, 1);
        AddHistoryFilterControl(grid, from, 3, 0); AddHistoryFilterControl(grid, to, 3, 1);
        AddHistoryFilterControl(grid, minWidth, 4, 0); AddHistoryFilterControl(grid, maxWidth, 4, 1);
        AddHistoryFilterControl(grid, minHeight, 5, 0); AddHistoryFilterControl(grid, maxHeight, 5, 1);
        AddHistoryFilterControl(grid, ocr, 6, 0); AddHistoryFilterControl(grid, barcode, 6, 1);
        AddHistoryFilterControl(grid, favorite, 7, 0);
        AddHistoryFilterControl(grid, workflow, 8, 0); AddHistoryFilterControl(grid, aiAction, 8, 1);
        var dialog = new ContentDialog { Title = "History filters", Content = grid, PrimaryButtonText = "Apply", SecondaryButtonText = "Clear", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            _historyQueryOptions = new HistoryQueryOptions();
            await ApplyHistoryFilterAsync();
            return;
        }
        if (result != ContentDialogResult.Primary) return;
        _historyQueryOptions = new HistoryQueryOptions(
            FromUtc: ToLocalDayBoundaryUtc(from.Date, endOfDay: false),
            ToUtc: ToLocalDayBoundaryUtc(to.Date, endOfDay: true),
            SourceOrAppContains: source.Text,
            WindowContains: window.Text,
            MonitorContains: monitor.Text,
            CaptureType: string.Equals(type.SelectedItem as string, "Any", StringComparison.OrdinalIgnoreCase) ? null : type.SelectedItem as string,
            MinWidth: ToPositiveInt(minWidth.Value), MaxWidth: ToPositiveInt(maxWidth.Value), MinHeight: ToPositiveInt(minHeight.Value), MaxHeight: ToPositiveInt(maxHeight.Value),
            HasOcr: ComboToNullableBool(ocr), HasBarcode: ComboToNullableBool(barcode), IsFavorite: ComboToNullableBool(favorite),
            SessionId: session.Text,
            Sort: sort.SelectedItem is HistorySortOrder order ? order : HistorySortOrder.Newest,
            WorkspaceId: _historyQueryOptions.WorkspaceId, FolderId: _historyQueryOptions.FolderId, CollectionId: _historyQueryOptions.CollectionId,
            WorkflowId: (workflow.SelectedItem as HistoryLibraryOption)?.Id, AiActionId: (aiAction.SelectedItem as HistoryLibraryOption)?.Id);
        await ApplyHistoryFilterAsync();
    }

    private static ComboBox CreateAnyYesNoCombo(string header, bool? value)
    {
        var combo = new ComboBox { Header = header, MinWidth = 150 };
        combo.Items.Add("Any"); combo.Items.Add("Yes"); combo.Items.Add("No");
        combo.SelectedIndex = value is null ? 0 : value.Value ? 1 : 2;
        return combo;
    }

    private static ComboBox CreateAnyStringCombo(string header, IEnumerable<string> values, string? selected)
    {
        var combo = new ComboBox { Header = header, MinWidth = 180 };
        var options = new[] { new HistoryLibraryOption("Any", null) }.Concat(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .Select(value => new HistoryLibraryOption(value, value))).ToArray();
        combo.ItemsSource = options;
        combo.SelectedItem = options.FirstOrDefault(option => string.Equals(option.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        return combo;
    }

    private static bool? ComboToNullableBool(ComboBox combo) => combo.SelectedIndex switch { 1 => true, 2 => false, _ => null };
    private static int? ToPositiveInt(double value) => double.IsFinite(value) && value > 0 ? (int)Math.Min(100000, Math.Round(value)) : null;
    private static DateTimeOffset? ToLocalDayBoundaryUtc(DateTimeOffset? value, bool endOfDay)
    {
        if (value is not { } date) return null;
        var local = endOfDay ? date.Date.AddDays(1).AddTicks(-1) : date.Date;
        return new DateTimeOffset(local, date.Offset).ToUniversalTime();
    }
    private static void AddHistoryFilterControl(Grid grid, FrameworkElement control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }


    private void RefreshHistoryOrganizationSelectors()
    {
        if (HistoryWorkspaceCombo is null || HistoryFolderCombo is null || HistoryCollectionCombo is null) return;
        _updatingHistoryOrganization = true;
        try
        {
            var workspaceOptions = new[] { new HistoryLibraryOption("All workspaces", null) }
                .Concat(_historyLibrarySnapshot.Workspaces.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => new HistoryLibraryOption(item.Name, item.Id))).ToArray();
            HistoryWorkspaceCombo.ItemsSource = workspaceOptions;
            HistoryWorkspaceCombo.SelectedItem = workspaceOptions.FirstOrDefault(item => item.Id == _historyQueryOptions.WorkspaceId) ?? workspaceOptions[0];

            var selectedWorkspaceId = (HistoryWorkspaceCombo.SelectedItem as HistoryLibraryOption)?.Id;
            var folders = _historyLibrarySnapshot.Folders.Where(folder => selectedWorkspaceId is null || folder.WorkspaceId == selectedWorkspaceId).ToArray();
            var folderOptions = new[] { new HistoryLibraryOption("All folders", null) }
                .Concat(folders.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).Select(folder =>
                {
                    var workspaceName = _historyLibrarySnapshot.Workspaces.FirstOrDefault(workspace => workspace.Id == folder.WorkspaceId)?.Name;
                    return new HistoryLibraryOption(selectedWorkspaceId is null && workspaceName is not null ? $"{workspaceName} / {folder.Name}" : folder.Name, folder.Id);
                })).ToArray();
            HistoryFolderCombo.ItemsSource = folderOptions;
            HistoryFolderCombo.SelectedItem = folderOptions.FirstOrDefault(item => item.Id == _historyQueryOptions.FolderId) ?? folderOptions[0];
            if ((HistoryFolderCombo.SelectedItem as HistoryLibraryOption)?.Id != _historyQueryOptions.FolderId) _historyQueryOptions = _historyQueryOptions with { FolderId = null };

            var collectionOptions = new[] { new HistoryLibraryOption("All collections", null) }
                .Concat(_historyLibrarySnapshot.Collections.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => new HistoryLibraryOption(item.Name, item.Id))).ToArray();
            HistoryCollectionCombo.ItemsSource = collectionOptions;
            HistoryCollectionCombo.SelectedItem = collectionOptions.FirstOrDefault(item => item.Id == _historyQueryOptions.CollectionId) ?? collectionOptions[0];
        }
        finally { _updatingHistoryOrganization = false; }
    }

    private async void HistoryOrganizationFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingHistoryOrganization || _services is null) return;
        if (ReferenceEquals(sender, HistoryWorkspaceCombo))
        {
            var selectedWorkspaceId = (HistoryWorkspaceCombo.SelectedItem as HistoryLibraryOption)?.Id;
            _historyQueryOptions = _historyQueryOptions with { WorkspaceId = selectedWorkspaceId, FolderId = null };
            RefreshHistoryOrganizationSelectors();
        }
        else if (ReferenceEquals(sender, HistoryFolderCombo))
        {
            _historyQueryOptions = _historyQueryOptions with { FolderId = (HistoryFolderCombo.SelectedItem as HistoryLibraryOption)?.Id };
        }
        else if (ReferenceEquals(sender, HistoryCollectionCombo))
        {
            _historyQueryOptions = _historyQueryOptions with { CollectionId = (HistoryCollectionCombo.SelectedItem as HistoryLibraryOption)?.Id };
        }
        await ApplyHistoryFilterAsync();
    }

    private void ApplyHistoryView(IReadOnlyList<HistoryDisplayItem> filtered)
    {
        var isTimeline = (HistoryViewCombo?.SelectedItem as ComboBoxItem)?.Tag as string == "timeline";
        HistoryList.Visibility = isTimeline ? Visibility.Collapsed : Visibility.Visible;
        HistoryTimelineList.Visibility = isTimeline ? Visibility.Visible : Visibility.Collapsed;
        if (!isTimeline)
        {
            HistoryList.ItemsSource = filtered;
            HistoryTimelineList.ItemsSource = null;
            return;
        }
        var rows = new List<HistoryTimelineRow>(filtered.Count);
        DateOnly? previous = null;
        foreach (var display in filtered.OrderByDescending(display => display.CreatedUtc))
        {
            var local = display.CreatedUtc.ToLocalTime();
            var day = DateOnly.FromDateTime(local.DateTime);
            var label = previous == day ? string.Empty : local.ToString("dddd, MMMM d, yyyy");
            rows.Add(new HistoryTimelineRow(label, display));
            previous = day;
        }
        HistoryTimelineList.ItemsSource = rows;
    }

    private IEnumerable<HistoryDisplayItem> ActiveSelectedHistoryDisplays()
    {
        if (HistoryTimelineList?.Visibility == Visibility.Visible)
            return HistoryTimelineList.SelectedItems.OfType<HistoryTimelineRow>().Select(row => row.Display);
        return HistoryList?.SelectedItems.OfType<HistoryDisplayItem>() ?? [];
    }

    private async void HistoryTimelineList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryTimelineRow row) await OpenHistoryItemAsync(row.Display.Item);
    }

    private void HistoryLibraryManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new HistoryLibraryManagerWindow(Services.HistoryLibrary, ActiveSelectedHistoryDisplays().Select(item => item.Item.Id));
        window.LibraryChanged += (_, _) => DispatcherQueue.TryEnqueue(async () => await RefreshHistoryAsync());
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private void History_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Import into Magic Capture History";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void History_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>(Math.Min(MaximumDroppedFiles, items.Count));
            foreach (var item in items)
            {
                if (paths.Count >= MaximumDroppedFiles) break;
                if (item is StorageFile file && IsSupportedHistoryImagePath(file.Path))
                {
                    paths.Add(file.Path);
                    continue;
                }
                if (item is StorageFolder folder && !string.IsNullOrWhiteSpace(folder.Path))
                {
                    try
                    {
                        paths.AddRange(Directory.EnumerateFiles(folder.Path, "*", SearchOption.TopDirectoryOnly)
                            .Where(IsSupportedHistoryImagePath)
                            .Take(MaximumDroppedFiles - paths.Count));
                    }
                    catch (IOException ex) { Services.Log.Error("HistoryDropFolder", ex); }
                    catch (UnauthorizedAccessException ex) { Services.Log.Error("HistoryDropFolder", ex); }
                }
            }
            if (paths.Count == 0) { ShowStatus("No supported images were found in the drop.", InfoBarSeverity.Warning); return; }
            await ImportHistoryPathsAsync(paths, "HistoryDrop");
        }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            Services.Log.Error("HistoryDrop", ex);
            ShowStatus("Dropped items could not be imported.", InfoBarSeverity.Warning);
        }
    }

    private static bool IsSupportedHistoryImagePath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private async Task ImportHistoryPathsAsync(IEnumerable<string> paths, string operationName)
    {
        var imported = 0;
        var failed = 0;
        foreach (var path in paths.Where(IsSupportedHistoryImagePath).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumDroppedFiles))
        {
            try
            {
                var sourceBytes = await ImageFileReader.ReadAsync(path);
                using var bitmap = BitmapCodec.Decode(sourceBytes);
                var png = BitmapCodec.EncodePng(bitmap);
                var asset = CaptureAsset.Create(new PixelRect(0, 0, bitmap.Width, bitmap.Height), png, CaptureSourceKind.Imported, Path.GetFileName(path));
                var item = await Services.HistoryStore.AddAsync(asset, Services.Settings with { HistoryEnabled = true });
                if (item is null) failed++; else imported++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                failed++;
                Services.Log.Error(operationName, ex);
            }
        }
        await RefreshHistoryAsync();
        ShowStatus($"Imported {imported} image(s)" + (failed > 0 ? $" · {failed} failed" : string.Empty), failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e) => await RefreshHistoryAsync();

    private async void HistorySessions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessions = await Services.HistoryStore.GetSessionsAsync();
            if (sessions.Count == 0) { ShowStatus("History has no capture sessions yet."); return; }
            var combo = new ComboBox { Header = "Capture session", MinWidth = 520, ItemsSource = sessions.Select(summary => new HistorySessionOption(summary)).ToArray() };
            combo.SelectedIndex = 0;
            var detail = new TextBlock
            {
                Text = $"{sessions.Count:N0} session(s) · {sessions.Sum(item => item.CaptureCount):N0} capture(s)",
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap
            };
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(detail);
            panel.Children.Add(combo);
            var dialog = new ContentDialog
            {
                Title = "History sessions",
                Content = panel,
                PrimaryButtonText = "Filter to session",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || combo.SelectedItem is not HistorySessionOption selected) return;
            if (selected.Summary.SessionId == HistorySessions.LegacySessionId)
            {
                ShowStatus("Legacy captures do not have a stored session id, so they cannot be isolated with the session filter.", InfoBarSeverity.Warning);
                return;
            }
            _historyQueryOptions = _historyQueryOptions with { SessionId = selected.Summary.SessionId };
            await ApplyHistoryFilterAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("HistorySessions", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void HistoryDuplicates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exact = await Services.HistoryStore.GetExactDuplicateGroupsAsync();
            var near = await Services.HistoryStore.GetNearDuplicateGroupsAsync(6);
            var lines = new List<string>
            {
                $"Exact duplicate groups: {exact.Count:N0}",
                $"Near-duplicate groups (dHash distance ≤ 6): {near.Count:N0}",
                string.Empty
            };
            foreach (var group in exact.Take(30))
                lines.Add($"EXACT · {group.Items.Count} captures · {group.Key[..Math.Min(12, group.Key.Length)]}…");
            foreach (var group in near.Take(30))
                lines.Add($"NEAR  · {group.Items.Count} captures · max distance {group.MaximumHammingDistance} · {group.Items[0].CreatedUtc.LocalDateTime:g}");
            if (exact.Count == 0 && near.Count == 0) lines.Add("No duplicate groups were found among captures that already have fingerprints.");
            var text = new TextBox
            {
                Text = string.Join("\r\n", lines),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Width = 680,
                Height = 380
            };
            var dialog = new ContentDialog { Title = "Duplicate inspector", Content = text, CloseButtonText = "Close", XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("HistoryDuplicates", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void HistoryDoctor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var health = await Services.HistoryStore.ScanHealthAsync();
            var summary = $"Rows missing primary PNG: {health.RowsWithoutPrimary.Count:N0}\n" +
                $"Recoverable orphan primary PNGs: {health.OrphanPrimaryPaths.Count:N0}\n" +
                $"Missing thumbnails: {health.MissingThumbnailItemIds.Count:N0}\n" +
                $"Orphan thumbnails: {health.OrphanThumbnailPaths.Count:N0}\n" +
                $"Missing fingerprints: {health.MissingFingerprintItemIds.Count:N0}\n\n" +
                "Primary PNG files are treated as the recovery source of truth. Derived thumbnails, fingerprints and the local search index may be rebuilt.";
            var dialog = new ContentDialog
            {
                Title = health.IssueCount == 0 ? "History Doctor · healthy" : $"History Doctor · {health.IssueCount:N0} issue(s)",
                Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
                PrimaryButtonText = health.IssueCount == 0 ? null : "Repair safely",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || health.IssueCount == 0) return;
            var result = await Services.HistoryStore.RepairAsync(Services.Settings);
            await RefreshHistoryAsync();
            ShowStatus(
                $"History repair finished · recovered {result.RecoveredPrimaryCount}, removed {result.RemovedMissingRows} broken row(s), rebuilt {result.RebuiltThumbnails} thumbnail(s), {result.RebuiltFingerprints} fingerprint(s), removed {result.RemovedOrphanThumbnails} orphan thumbnail(s)" +
                (result.FailureCount > 0 ? $" · {result.FailureCount} operation(s) failed" : string.Empty),
                result.FailureCount == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("HistoryDoctor", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void HistoryArchiveExport_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistoryDisplays();
        var picker = new FileSavePicker { SuggestedFileName = $"Magic-Capture-History-{DateTime.Now:yyyyMMdd-HHmmss}" };
        picker.FileTypeChoices.Add("Magic Capture History", new List<string> { ".magichistory" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            var count = await Services.HistoryArchive.ExportAsync(file.Path, selected.Length == 0 ? null : selected.Select(item => item.Item.Id), CurrentAppVersion());
            ShowStatus($"Exported {count:N0} History capture(s) to a verified .magichistory archive.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Services.Log.Error("HistoryArchiveExport", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void HistoryArchiveImport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".magichistory");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var confirm = new ContentDialog
        {
            Title = "Import History archive?",
            Content = "The archive will be fully validated against its manifest, byte budgets and SHA-256 inventory before each capture is merged. Imported captures receive new local IDs.",
            PrimaryButtonText = "Validate and import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var result = await Services.HistoryArchive.ImportAsync(file.Path, Services.Settings);
            await RefreshHistoryAsync();
            ShowStatus($"History import finished · {result.Imported:N0} imported" + (result.Failed > 0 ? $" · {result.Failed:N0} failed" : string.Empty),
                result.Failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("HistoryArchiveImport", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void ConfigurationArchiveExport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"Magic-Capture-Configuration-{DateTime.Now:yyyyMMdd-HHmmss}" };
        picker.FileTypeChoices.Add("Magic Capture configuration", new List<string> { ".magicconfig" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await Services.ConfigurationArchive.ExportAsync(file.Path, Services.Settings, CurrentAppVersion());
            ShowStatus("Exported local configuration. Secrets, commerce state, Local Action approvals, logs and caches were intentionally excluded.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Services.Log.Error("ConfigurationArchiveExport", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void ConfigurationArchiveImport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".magicconfig");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var confirm = new ContentDialog
        {
            Title = "Replace local configuration?",
            Content = "Every payload will be validated first, then the allowlisted configuration files will be committed as one rollback-capable transaction. Credentials and executable approvals are never imported.",
            PrimaryButtonText = "Validate and import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var result = await ((App)Application.Current).ImportConfigurationAsync(file.Path);
            await RefreshWorkflowsAsync();
            await RefreshLocalActionsAsync();
            await RefreshDestinationsAsync();
            await RefreshAiSettingsAsync();
            var importMessage = $"Imported {result.ImportedFiles:N0} validated configuration file(s). Secrets and trust approvals were left unchanged.";
            if (!string.IsNullOrWhiteSpace(result.Warning)) importMessage += " " + result.Warning;
            ShowStatus(importMessage, string.IsNullOrWhiteSpace(result.Warning) ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("ConfigurationArchiveImport", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void RepairAiCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = await Services.AiCache.RepairAsync(TimeSpan.FromDays(Services.Settings.AiCacheMaximumAgeDays), Services.Settings.AiCacheMaximumEntries);
            ShowStatus($"AI cache repair · scanned {report.Scanned:N0}, kept {report.Kept:N0}, deleted {report.Deleted:N0}, ancillary {report.AncillaryDeleted:N0}" +
                (report.Failed > 0 ? $" · {report.Failed:N0} failed" : string.Empty), report.Failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Services.Log.Error("AiCacheRepair", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
    }

    private static string CurrentAppVersion() =>
        typeof(App).Assembly.GetName().Version is { } version ? $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}" : "3.9.0";

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Services.HistoryStore.ClearAsync();
            await RefreshHistoryAsync();
        }
        catch (Exception ex) { Services.Log.Error("HistoryClear", ex); }
    }

    private HistoryDisplayItem[] SelectedHistoryDisplays() => ActiveSelectedHistoryDisplays().Take(5_000).ToArray();

    private async void HistoryBatchDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistoryDisplays();
        if (selected.Length == 0) { ShowStatus("Select one or more History captures first.", InfoBarSeverity.Warning); return; }
        var dialog = new ContentDialog { Title = $"Delete {selected.Length} capture(s)?", Content = "This removes the local History images and thumbnails. This cannot be undone.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var removed = await Services.HistoryStore.DeleteManyAsync(selected.Select(display => display.Item.Id));
            ShowStatus($"Deleted {removed} capture(s).", InfoBarSeverity.Success);
            await RefreshHistoryAsync();
        }
        catch (Exception ex) { Services.Log.Error("HistoryBatchDelete", ex); ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }

    private void WorkflowTriggerManager_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.AdvancedWorkflows)) return;
        var window = new WorkflowTriggerManagerWindow(Services);
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private async void HistoryBatchTag_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistoryDisplays();
        if (selected.Length == 0) { ShowStatus("Select one or more History captures first.", InfoBarSeverity.Warning); return; }
        var box = new TextBox { Header = $"Add tags to {selected.Length} capture(s)", PlaceholderText = "bug, documentation, release" };
        var dialog = new ContentDialog { Title = "Batch tag", Content = box, PrimaryButtonText = "Add tags", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var tags = box.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var changed = await Services.HistoryStore.AddTagsAsync(selected.Select(display => display.Item.Id), tags);
        ShowStatus($"Updated {changed} capture(s).", changed > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        await RefreshHistoryAsync();
    }

    private async void HistoryBatchExport_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedHistoryDisplays();
        if (selected.Length == 0) { ShowStatus("Select one or more History captures first.", InfoBarSeverity.Warning); return; }
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || string.IsNullOrWhiteSpace(folder.Path)) return;
        var exported = 0;
        var failed = 0;
        foreach (var display in selected)
        {
            try
            {
                var source = Services.HistoryStore.GetAbsolutePath(display.Item);
                if (!File.Exists(source)) { failed++; continue; }
                var baseName = string.IsNullOrWhiteSpace(display.Item.Title) ? Path.GetFileNameWithoutExtension(source) : display.Item.Title!;
                var destination = GetCollisionSafePath(folder.Path, SanitizeBatchFileName(baseName) + ".png");
                await CopyFileExclusiveAsync(source, destination);
                exported++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                failed++;
                Services.Log.Error("HistoryBatchExport", ex);
            }
        }
        ShowStatus($"Exported {exported} capture(s)" + (failed > 0 ? $" · {failed} failed" : string.Empty), failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async void HistoryImport_Click(object sender, RoutedEventArgs e)
    {
        var files = await CreateImagePicker().PickMultipleFilesAsync();
        if (files.Count == 0) return;
        await ImportHistoryPathsAsync(files.Take(MaximumDroppedFiles).Select(file => file.Path), "HistoryImport");
    }

    private static async Task CopyFileExclusiveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static string SanitizeBatchFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = "capture";
        return clean.Length <= 120 ? clean : clean[..120];
    }

    private static string GetCollisionSafePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix <= 10_000; suffix++)
        {
            candidate = Path.Combine(folder, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not create a collision-safe export filename.");
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryDisplayItem display) await OpenHistoryItemAsync(display.Item);
    }

    private async void HistoryOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryDisplayItem display) await OpenHistoryItemAsync(display.Item);
    }

    private void HistoryReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryDisplayItem display) return;
        var path = Services.HistoryStore.GetAbsolutePath(display.Item);
        if (!File.Exists(path)) return;
        using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private async void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryDisplayItem display) return;
        await Services.HistoryStore.DeleteAsync(display.Item.Id);
        await RefreshHistoryAsync();
    }

    private async void HistoryDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryDisplayItem display) return;
        var item = display.Item;
        var title = new TextBox { Header = "Title", Text = item.Title ?? string.Empty, MaxLength = HistoryMetadata.MaxTitleLength };
        var tags = new TextBox
        {
            Header = "Tags",
            Text = item.Tags is null ? string.Empty : string.Join(", ", item.Tags),
            PlaceholderText = "bug, documentation, release"
        };
        var notes = new TextBox
        {
            Header = "Notes",
            Text = item.Notes ?? string.Empty,
            MaxLength = HistoryMetadata.MaxNotesLength,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100
        };
        var favorite = new ToggleSwitch { Header = "Favorite", IsOn = item.IsFavorite };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(title);
        panel.Children.Add(tags);
        panel.Children.Add(notes);
        panel.Children.Add(favorite);
        var dialog = new ContentDialog
        {
            Title = "Capture details",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var update = HistoryMetadata.Normalize(
            title.Text,
            notes.Text,
            tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            favorite.IsOn);
        await Services.HistoryStore.UpdateMetadataAsync(item.Id, update);
        await RefreshHistoryAsync();
    }

    private async Task RecordHistoryOpenedBestEffortAsync(Guid assetId, CancellationToken cancellationToken)
    {
        try { await Services.HistoryLibrary.RecordOpenedAsync(assetId, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
        {
            Services.Log.Error("HistoryOpenedActivity", ex);
        }
    }

    private async Task OpenHistoryItemAsync(HistoryItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            await RecordHistoryOpenedBestEffortAsync(item.Id, cancellationToken);
            var path = Services.HistoryStore.GetAbsolutePath(item);
            if (!File.Exists(path)) return;
            var bytes = await ImageFileReader.ReadAsync(path, cancellationToken);
            ImageWorkloadLimits.ValidateEncodedLength(bytes.LongLength);
            _ = Enum.TryParse<CaptureSourceKind>(item.SourceKind, out var kind);
            var asset = new CaptureAsset(item.Id, item.CreatedUtc, new Magic.Capture.Core.Geometry.PixelRect(0, 0, item.Width, item.Height), bytes, item.Width, item.Height, kind, item.SourceDisplayName ?? "History", item.WindowTitle, item.ProcessName, item.MonitorName, ExecutablePath: item.ExecutablePath);
            ((App)Application.Current).OpenResult(asset);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Log.Error("HistoryOpen", ex);
        }
    }

    private bool EnsurePlus(ProductFeature feature)
    {
        if (Services.Entitlements.CanUse(feature)) return true;
        ShowPlan(feature);
        return false;
    }

    private bool EnsurePro(ProductFeature feature)
    {
        if (Services.Entitlements.CanUse(feature)) return true;
        ShowPlan(feature);
        return false;
    }

    private async void AddStitchImages_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.ScrollingStitch)) return;
        try
        {
            var picker = CreateImagePicker();
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                _stitchFrames.Add(await ImageFileReader.ReadAsync(file.Path));
                _stitchNames.Add(file.Name);
            }
            StitchFrameList.ItemsSource = _stitchNames.ToArray();
            StitchStatusText.Text = $"{_stitchFrames.Count} frame(s) loaded.";
        }
        catch (Exception ex) { StitchStatusText.Text = ex.Message; Services.Log.Error("StitchAdd", ex); }
    }

    private FileOpenPicker CreateImagePicker()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        return picker;
    }

    private void ClearStitch_Click(object sender, RoutedEventArgs e)
    {
        _stitchFrames.Clear();
        _stitchNames.Clear();
        StitchFrameList.ItemsSource = null;
        StitchStatusText.Text = "Frames cleared.";
    }

    private void Stitch_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlus(ProductFeature.ScrollingStitch)) return;
        try
        {
            var result = Services.Stitcher.Stitch(_stitchFrames);
            using var bitmap = BitmapCodec.Decode(result.PngBytes);
            var asset = CaptureAsset.Create(new Magic.Capture.Core.Geometry.PixelRect(0, 0, bitmap.Width, bitmap.Height), result.PngBytes, CaptureSourceKind.Stitch, "Vertical Stitch");
            StitchStatusText.Text = string.Join(" • ", result.Pairs.Select(pair => $"{pair.UpperIndex + 1}→{pair.LowerIndex + 1}: overlap {pair.Match.OverlapRows}px, diff {pair.Match.MeanAbsoluteDifference:F1}"));
            ((App)Application.Current).OpenResult(asset);
        }
        catch (Exception ex) { StitchStatusText.Text = ex.Message; Services.Log.Error("Stitch", ex); }
    }

    private async void ComparePickA_Click(object sender, RoutedEventArgs e) => await PickCompareImageAsync(true);
    private async void ComparePickB_Click(object sender, RoutedEventArgs e) => await PickCompareImageAsync(false);

    private async Task PickCompareImageAsync(bool first)
    {
        if (!Services.Entitlements.CanUse(ProductFeature.CompareWorkspace)) { ShowPlan(ProductFeature.CompareWorkspace); return; }
        var file = await CreateImagePicker().PickSingleFileAsync();
        if (file is null) return;
        var bytes = await ImageFileReader.ReadAsync(file.Path);
        if (first) { _compareA = bytes; _compareAName = file.Name; CompareAText.Text = file.Name; }
        else { _compareB = bytes; _compareBName = file.Name; CompareBText.Text = file.Name; }
    }

    private void CompareOpen_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.Entitlements.CanUse(ProductFeature.CompareWorkspace)) { ShowPlan(ProductFeature.CompareWorkspace); return; }
        if (_compareA is null || _compareB is null) { ShowStatus("Choose both comparison images first.", InfoBarSeverity.Warning); return; }
        var window = new CompareWindow(_compareA, _compareAName ?? "A", _compareB, _compareBName ?? "B", Services);
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private async void CompareLatestHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.Entitlements.CanUse(ProductFeature.CompareWorkspace)) { ShowPlan(ProductFeature.CompareWorkspace); return; }
        try
        {
            var items = await Services.HistoryStore.ListAsync();
            if (items.Count < 2) { ShowStatus("History needs at least two captures.", InfoBarSeverity.Warning); return; }
            var latest = items[0];
            var previous = items.Skip(1).FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(latest.WindowTitle) && string.Equals(item.WindowTitle, latest.WindowTitle, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(latest.SourceDisplayName) && string.Equals(item.SourceDisplayName, latest.SourceDisplayName, StringComparison.OrdinalIgnoreCase))) ?? items[1];
            _compareA = await ImageFileReader.ReadAsync(Services.HistoryStore.GetAbsolutePath(previous));
            _compareB = await ImageFileReader.ReadAsync(Services.HistoryStore.GetAbsolutePath(latest));
            _compareAName = previous.Title ?? previous.WindowTitle ?? $"History {previous.CreatedUtc.LocalDateTime:g}";
            _compareBName = latest.Title ?? latest.WindowTitle ?? $"History {latest.CreatedUtc.LocalDateTime:g}";
            CompareAText.Text = _compareAName;
            CompareBText.Text = _compareBName;
            ShowStatus("Loaded latest History item and the closest previous version.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Log.Error("CompareHistory", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void CompareBatch_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.Entitlements.CanUse(ProductFeature.CompareWorkspace)) { ShowPlan(ProductFeature.CompareWorkspace); return; }
        try
        {
            var files = (await CreateImagePicker().PickMultipleFilesAsync()).Take(32).ToArray();
            if (files.Length < 2) { ShowStatus("Choose 2–32 images. The first image is the baseline.", InfoBarSeverity.Warning); return; }
            var baseline = await ImageFileReader.ReadAsync(files[0].Path);
            var rows = new List<string>();
            for (var index = 1; index < files.Length; index++)
            {
                var candidate = await ImageFileReader.ReadAsync(files[index].Path);
                var result = await Task.Run(() => new ImageCompareService().Compare(baseline, candidate, new ImageDifferenceOptions(), autoAlignTranslation: true));
                rows.Add($"<tr><td>{System.Net.WebUtility.HtmlEncode(files[index].Name)}</td><td>{result.ChangedPixelPercent:F3}%</td><td>{result.StructuralSimilarity:F6}</td><td>{result.PerceptualHashDistance}/64</td><td>{result.AlignmentOffsetX},{result.AlignmentOffsetY}</td></tr>");
            }
            var picker = new FileSavePicker { SuggestedFileName = "Magic Capture Desktop_Batch_Compare" };
            picker.FileTypeChoices.Add("HTML report", [".html"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
            var output = await picker.PickSaveFileAsync();
            if (output is null) return;
            var html = $"<!doctype html><meta charset='utf-8'><title>Magic Capture Desktop Batch Compare</title><style>body{{font:14px system-ui;max-width:1000px;margin:32px auto}}table{{border-collapse:collapse}}td,th{{border:1px solid #bbb;padding:6px 10px}}</style><h1>Batch Compare</h1><p>Baseline: {System.Net.WebUtility.HtmlEncode(files[0].Name)}</p><table><tr><th>Image</th><th>Changed</th><th>SSIM</th><th>dHash</th><th>Shift</th></tr>{string.Join(string.Empty, rows)}</table><p>Generated locally by Magic Capture Desktop.</p>";
            await File.WriteAllTextAsync(output.Path, html);
            ShowStatus($"Batch report saved: {output.Name}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Services.Log.Error("CompareBatch", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }


    private bool EnsureProAi(ProductFeature feature = ProductFeature.AiProviders)
    {
        if (Services.Entitlements.CanUse(feature)) return true;
        ShowPlan(feature);
        return false;
    }

    private async Task RefreshAiSettingsAsync()
    {
        if (_services is null) return;
        try
        {
            var state = await Services.AiProfiles.LoadAsync();
            _aiProfiles = state.Profiles;
            _updatingAiProfile = true;
            AiProfileCombo.ItemsSource = _aiProfiles;
            AiProfileCombo.SelectedItem = _aiProfiles.FirstOrDefault(p => p.Id == state.ActiveProfileId) ?? _aiProfiles.FirstOrDefault();
            AiPreferTextCheck.IsChecked = state.Privacy.PreferTextOnlyWhenPossible;
            AiNeverCloudImagesCheck.IsChecked = state.Privacy.NeverSendImagesToCloud;
            AiLocalOnlyCheck.IsChecked = state.Privacy.LocalProvidersOnly;
            AiPayloadConfirmCheck.IsChecked = state.Privacy.ShowPayloadSummaryBeforeCloudAction;
            SelectTag(AiRoutingModeCombo, state.Privacy.RoutingMode.ToString());
            _customActions = await Services.MagicActionStore.LoadAsync();
            CustomActionList.ItemsSource = _customActions;
            _magicRecipes = await Services.MagicRecipeStore.LoadAsync();
            MagicRecipeList.ItemsSource = _magicRecipes;
            AiContextStatusText.Text = $"{Services.AiContext.Count} / 8 captures";
            _updatingAiProfile = false;
            if (AiProfileCombo.SelectedItem is AiProviderProfile profile) PopulateAiProfile(profile);
            ApplyAiEntitlement();
        }
        catch (Exception ex)
        {
            _updatingAiProfile = false;
            AiProviderStatusText.Text = ex.Message;
            Services.Log.Error("AiSettingsRefresh", ex);
        }
    }

    private void ApplyAiEntitlement()
    {
        if (_services is null) return;
        var pro = Services.Entitlements.CanUse(ProductFeature.AiProviders);
        AiLockedInfo.IsOpen = !pro;
        AiConfigurationPanel.IsHitTestVisible = pro;
    }

    private void PopulateAiProfile(AiProviderProfile profile)
    {
        _updatingAiProfile = true;
        AiProviderKindCombo.SelectedItem = profile.Kind;
        AiProfileNameBox.Text = profile.DisplayName;
        AiBaseUriBox.Text = profile.BaseUri;
        AiModelBox.Text = profile.ModelId;
        AiKeyBox.Password = string.Empty;
        AiVisionCheck.IsChecked = profile.Capabilities.HasFlag(AiCapability.VisionInput);
        AiMultiImageCheck.IsChecked = profile.Capabilities.HasFlag(AiCapability.MultipleImages);
        AiStructuredCheck.IsChecked = profile.Capabilities.HasFlag(AiCapability.StructuredJson);
        AiReasoningCheck.IsChecked = profile.Capabilities.HasFlag(AiCapability.Reasoning);
        SelectTag(AiContextSizeCombo, profile.ContextSize.ToString());
        SelectTag(AiVisionQualityCombo, profile.VisionQuality.ToString());
        _updatingAiProfile = false;
    }

    private static void SelectTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag as string, tag, StringComparison.Ordinal));
    }

    private void AiProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAiProfile) return;
        if (AiProfileCombo.SelectedItem is AiProviderProfile profile) PopulateAiProfile(profile);
    }

    private void AiProviderKindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAiProfile || AiProviderKindCombo.SelectedItem is not AiProviderKind kind) return;
        var current = AiProviderRegistry.CreateDefault(kind);
        AiProfileNameBox.Text = current.DisplayName;
        AiBaseUriBox.Text = current.BaseUri;
        AiModelBox.Text = current.ModelId;
        AiVisionCheck.IsChecked = current.Capabilities.HasFlag(AiCapability.VisionInput);
        AiMultiImageCheck.IsChecked = current.Capabilities.HasFlag(AiCapability.MultipleImages);
        AiStructuredCheck.IsChecked = current.Capabilities.HasFlag(AiCapability.StructuredJson);
        AiReasoningCheck.IsChecked = current.Capabilities.HasFlag(AiCapability.Reasoning);
        SelectTag(AiVisionQualityCombo, current.VisionQuality.ToString());
    }

    private void AiNewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi()) return;
        var kind = AiProviderKind.OpenAI;
        var profile = AiProviderRegistry.CreateDefault(kind);
        AiProfileCombo.SelectedItem = null;
        PopulateAiProfile(profile);
        AiProviderStatusText.Text = "New profile — save when ready.";
    }

    private AiProviderProfile BuildAiProfileFromUi()
    {
        var existing = AiProfileCombo.SelectedItem as AiProviderProfile;
        var kind = AiProviderKindCombo.SelectedItem is AiProviderKind selected ? selected : AiProviderKind.OpenAI;
        var defaultProfile = AiProviderRegistry.CreateDefault(kind);
        var capabilities = AiCapability.TextInput;
        if (AiVisionCheck.IsChecked == true) capabilities |= AiCapability.VisionInput;
        if (AiMultiImageCheck.IsChecked == true) capabilities |= AiCapability.MultipleImages;
        if (AiStructuredCheck.IsChecked == true) capabilities |= AiCapability.StructuredJson;
        if (AiReasoningCheck.IsChecked == true) capabilities |= AiCapability.Reasoning;
        if (kind is AiProviderKind.Ollama or AiProviderKind.LmStudio) capabilities |= AiCapability.LocalEndpoint;
        _ = Enum.TryParse<AiContextSizeClass>((AiContextSizeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var contextSize);
        _ = Enum.TryParse<AiVisionQuality>((AiVisionQualityCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var visionQuality);
        return new AiProviderProfile(
            existing?.Id ?? Guid.NewGuid(),
            string.IsNullOrWhiteSpace(AiProfileNameBox.Text) ? defaultProfile.DisplayName : AiProfileNameBox.Text.Trim(),
            kind,
            string.IsNullOrWhiteSpace(AiBaseUriBox.Text) ? defaultProfile.BaseUri : AiBaseUriBox.Text.Trim(),
            string.IsNullOrWhiteSpace(AiModelBox.Text) ? defaultProfile.ModelId : AiModelBox.Text.Trim(),
            capabilities,
            contextSize,
            visionQuality,
            existing?.SecretId ?? $"ai-{Guid.NewGuid():N}");
    }

    private async void AiSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi()) return;
        try
        {
            var profile = BuildAiProfileFromUi();
            if (!AiEndpointPolicy.TryValidate(profile.BaseUri, out _)) throw new InvalidOperationException(AiEndpointPolicy.ErrorMessage);
            var state = await Services.AiProfiles.LoadAsync();
            var profiles = state.Profiles.Where(p => p.Id != profile.Id).Append(profile).ToArray();
            if (!string.IsNullOrWhiteSpace(AiKeyBox.Password)) await Services.AiSecrets.SaveAsync(profile.SecretId, AiKeyBox.Password);
            await Services.AiProfiles.SaveAsync(state with { Profiles = profiles, ActiveProfileId = profile.Id });
            AiKeyBox.Password = string.Empty;
            AiProviderStatusText.Text = "Profile saved and selected.";
            await RefreshAiSettingsAsync();
        }
        catch (Exception ex) { AiProviderStatusText.Text = ex.Message; Services.Log.Error("AiProfileSave", ex); }
    }

    private async void AiDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi() || AiProfileCombo.SelectedItem is not AiProviderProfile profile) return;
        var state = await Services.AiProfiles.LoadAsync();
        var profiles = state.Profiles.Where(p => p.Id != profile.Id).ToArray();
        await Services.AiProfiles.SaveAsync(state with { Profiles = profiles, ActiveProfileId = state.ActiveProfileId == profile.Id ? profiles.FirstOrDefault()?.Id : state.ActiveProfileId });
        await Services.AiSecrets.DeleteAsync(profile.SecretId);
        await RefreshAiSettingsAsync();
    }

    private async void AiTestProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi()) return;
        try
        {
            var profile = BuildAiProfileFromUi();
            if (!AiEndpointPolicy.TryValidate(profile.BaseUri, out _)) throw new InvalidOperationException(AiEndpointPolicy.ErrorMessage);
            if (!string.IsNullOrWhiteSpace(AiKeyBox.Password)) await Services.AiSecrets.SaveAsync(profile.SecretId, AiKeyBox.Password);
            AiProviderStatusText.Text = "Testing…";
            var result = await Services.AiClients.Create(profile).ProbeAsync();
            AiProviderStatusText.Text = result.Message;
        }
        catch (Exception ex) { AiProviderStatusText.Text = ex.Message; Services.Log.Error("AiProviderTest", ex); }
    }

    private async void AiDiscoverModels_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi()) return;
        try
        {
            var profile = BuildAiProfileFromUi();
            if (!AiEndpointPolicy.TryValidate(profile.BaseUri, out _)) throw new InvalidOperationException(AiEndpointPolicy.ErrorMessage);
            if (!string.IsNullOrWhiteSpace(AiKeyBox.Password)) await Services.AiSecrets.SaveAsync(profile.SecretId, AiKeyBox.Password);
            AiProviderStatusText.Text = "Discovering models…";
            var models = await Services.AiClients.Create(profile).ListModelsAsync();
            AiDiscoveredModelsCombo.ItemsSource = models;
            AiProviderStatusText.Text = models.Count == 0 ? "Connection works, but no models were returned." : $"Discovered {models.Count} model(s).";
        }
        catch (Exception ex) { AiProviderStatusText.Text = ex.Message; Services.Log.Error("AiModelDiscovery", ex); }
    }

    private void AiDiscoveredModelsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AiDiscoveredModelsCombo.SelectedItem is string model) AiModelBox.Text = model;
    }

    private async void AiSavePrivacy_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi()) return;
        var state = await Services.AiProfiles.LoadAsync();
        _ = Enum.TryParse<AiRoutingMode>((AiRoutingModeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var routingMode);
        var privacy = new AiPrivacySettings(
            AiPreferTextCheck.IsChecked == true,
            AiNeverCloudImagesCheck.IsChecked == true,
            AiLocalOnlyCheck.IsChecked == true,
            AiPayloadConfirmCheck.IsChecked != false,
            routingMode);
        await Services.AiProfiles.SaveAsync(state with { Privacy = privacy });
        AiProviderStatusText.Text = "AI privacy settings saved.";
    }

    private async void SaveCustomAction_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.CustomMagicActions)) return;
        try
        {
            var name = CustomActionNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(CustomActionPromptBox.Text)) throw new InvalidOperationException("Name and instruction are required.");
            var id = "custom." + string.Concat(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-') + "." + Guid.NewGuid().ToString("N")[..6];
            var action = new MagicActionDefinition(id, name, string.IsNullOrWhiteSpace(CustomActionCategoryBox.Text) ? "Custom" : CustomActionCategoryBox.Text.Trim(),
                "Follow the custom instruction using only supplied screen context. Do not invent source evidence.", CustomActionPromptBox.Text.Trim(),
                AiCapability.TextInput, AiCapability.TextInput | (CustomActionVisionCheck.IsChecked == true ? AiCapability.VisionInput : AiCapability.None),
                CustomActionVisionCheck.IsChecked == true ? MagicActionVisionMode.Optional : MagicActionVisionMode.None,
                MagicActionOutputKind.Markdown, true, true, false);
            var list = _customActions.Append(action).ToArray();
            await Services.MagicActionStore.SaveAsync(list);
            CustomActionNameBox.Text = string.Empty; CustomActionPromptBox.Text = string.Empty;
            await RefreshAiSettingsAsync();
        }
        catch (Exception ex) { AiProviderStatusText.Text = ex.Message; }
    }

    private async void DeleteCustomAction_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.CustomMagicActions) || CustomActionList.SelectedItem is not MagicActionDefinition selected) return;
        try
        {
            var workflows = await Services.Workflows.LoadAsync();
            var dependents = WorkflowReferencePolicy.FindMagicActionDependents(selected.Id, workflows, _magicRecipes);
            if (dependents.Count > 0)
                throw new InvalidOperationException("Magic Action is still referenced by " + string.Join(", ", dependents) + ". Remove those references first.");
            await Services.MagicActionStore.SaveAsync(_customActions.Where(a => a.Id != selected.Id).ToArray());
            var cleaned = await ((App)Application.Current).TryMutateSettingsAsync(
                current => SettingsReferencePolicy.RemoveMagicActionReferences(current, selected.Id),
                SettingsRuntimeEffects.MainWindowUi,
                logComponent: "MagicActionDeleteSettingsCleanup");
            await RefreshAiSettingsAsync();
            if (!cleaned) AiProviderStatusText.Text = "Magic Action was deleted, but its hotkey cleanup could not be persisted. Restart before editing hotkeys.";
        }
        catch (Exception ex)
        {
            Services.Log.Error("MagicActionDelete", ex);
            AiProviderStatusText.Text = ex.Message;
        }
    }

    private async void ImportCustomAction_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.CustomMagicActions)) return;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".magicaction");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var action = await Services.MagicActionStore.ImportAsync(file.Path);
            await Services.MagicActionStore.SaveAsync(_customActions.Where(a => a.Id != action.Id).Append(action).ToArray());
            await RefreshAiSettingsAsync();
        }
        catch (Exception ex) { AiProviderStatusText.Text = ex.Message; }
    }

    private async void ExportCustomAction_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.CustomMagicActions) || CustomActionList.SelectedItem is not MagicActionDefinition selected) return;
        var picker = new FileSavePicker { SuggestedFileName = selected.Name.Replace(' ', '-') };
        picker.FileTypeChoices.Add("Magic Action", new List<string> { ".magicaction" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is not null) await Services.MagicActionStore.ExportAsync(selected, file.Path);
    }

    private void MagicRecipeNew_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.MagicRecipes)) return;
        _editingMagicRecipeId = null;
        MagicRecipeList.SelectedItem = null;
        MagicRecipeNameBox.Text = string.Empty;
        MagicRecipeStepsBox.Text = "STEP:RunOcr\r\nSTEP:ExtractSignals\r\nAI:general.explain";
        MagicRecipeStatusText.Text = "New recipe.";
    }

    private void MagicRecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MagicRecipeList.SelectedItem is not MagicRecipe recipe) return;
        _editingMagicRecipeId = recipe.Id;
        MagicRecipeNameBox.Text = recipe.Name;
        MagicRecipeStepsBox.Text = string.Join("\r\n", recipe.Steps.Select(FormatMagicRecipeStep));
        MagicRecipeStatusText.Text = $"{recipe.Steps.Count} step(s).";
    }

    private async void MagicRecipeSave_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.MagicRecipes)) return;
        try
        {
            var recipe = BuildMagicRecipeFromUi();
            await Services.MagicRecipeStore.SaveAsync(_magicRecipes.Where(r => r.Id != recipe.Id).Append(recipe));
            _editingMagicRecipeId = recipe.Id;
            await RefreshAiSettingsAsync();
            MagicRecipeList.SelectedItem = _magicRecipes.FirstOrDefault(r => r.Id == recipe.Id);
            MagicRecipeStatusText.Text = "Recipe saved locally.";
        }
        catch (Exception ex) { MagicRecipeStatusText.Text = ex.Message; }
    }

    private async void MagicRecipeDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.MagicRecipes) || MagicRecipeList.SelectedItem is not MagicRecipe selected) return;
        await Services.MagicRecipeStore.SaveAsync(_magicRecipes.Where(r => r.Id != selected.Id));
        MagicRecipeNew_Click(sender, e);
        await RefreshAiSettingsAsync();
    }

    private async void MagicRecipeImport_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.MagicRecipes)) return;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".magicrecipe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var recipe = await Services.MagicRecipeStore.ImportAsync(file.Path);
            await Services.MagicRecipeStore.SaveAsync(_magicRecipes.Where(r => r.Id != recipe.Id).Append(recipe));
            await RefreshAiSettingsAsync();
            MagicRecipeList.SelectedItem = _magicRecipes.FirstOrDefault(r => r.Id == recipe.Id);
        }
        catch (Exception ex) { MagicRecipeStatusText.Text = ex.Message; }
    }

    private async void MagicRecipeExport_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.MagicRecipes) || MagicRecipeList.SelectedItem is not MagicRecipe selected) return;
        var picker = new FileSavePicker { SuggestedFileName = selected.Name.Replace(' ', '-') };
        picker.FileTypeChoices.Add("Magic Recipe", new List<string> { ".magicrecipe" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is not null) await Services.MagicRecipeStore.ExportAsync(selected, file.Path);
    }

    private async void MagicRecipeRun_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.MagicRecipes) || MagicRecipeList.SelectedItem is not MagicRecipe recipe) return;
        var asset = (await SelectedHistoryAssetsAsync(maximumCount: 1)).FirstOrDefault();
        if (asset is null) { MagicRecipeStatusText.Text = "Select a capture in History first."; return; }
        if (Services.MagicRecipes is null) { MagicRecipeStatusText.Text = "Recipe service is unavailable."; return; }
        try
        {
            var result = await Services.MagicRecipes.ExecuteAsync(recipe, new WorkflowExecutionContext(
                asset,
                SaveImageAsync: async (image, _) =>
                {
                    await Services.Export.SaveImageAsAsync(this, image, "png", Services.Settings.JpegQuality, Services.Settings.FileNameTemplate);
                },
                PinImage: image => ((App)Application.Current).OpenPin(image),
                OpenEditor: image => ((App)Application.Current).OpenAnnotation(image),
                AiContext: Services.AiContext.Assets,
                ConfirmCloudMagicActionAsync: ((App)Application.Current).ConfirmWorkflowCloudAiAsync));
            var completed = result.Steps.Count(step => step.Succeeded);
            var failed = result.Steps.FirstOrDefault(step => !step.Succeeded);
            MagicRecipeStatusText.Text = result.Succeeded ? $"Recipe completed: {completed} step(s)." : failed?.Message ?? "Recipe failed.";
        }
        catch (Exception ex) { MagicRecipeStatusText.Text = ex.Message; }
    }

    private MagicRecipe BuildMagicRecipeFromUi()
    {
        var name = MagicRecipeNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Recipe name is required.");
        var id = _editingMagicRecipeId ?? "recipe." + string.Concat(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-') + "." + Guid.NewGuid().ToString("N")[..6];
        var lines = MagicRecipeStepsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var steps = new List<MagicRecipeStep>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("AI:", StringComparison.OrdinalIgnoreCase))
            {
                var actionId = line[3..].Trim();
                steps.Add(new MagicRecipeStep($"s{i + 1}", MagicRecipeStepKind.MagicAction, actionId, null));
                continue;
            }
            if (line.StartsWith("STEP:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[5..].Trim();
                var separator = payload.IndexOf(':');
                var reference = separator < 0 ? payload : payload[..separator];
                var argument = separator < 0 ? null : payload[(separator + 1)..];
                IReadOnlyDictionary<string, string>? options = string.IsNullOrWhiteSpace(argument) ? null : new Dictionary<string, string> { ["argument"] = argument };
                steps.Add(new MagicRecipeStep($"s{i + 1}", MagicRecipeStepKind.WorkflowStep, reference, options));
                continue;
            }
            throw new InvalidOperationException($"Invalid recipe line {i + 1}: use AI:action-id or STEP:WorkflowStepKind[:argument].");
        }
        var recipe = new MagicRecipe(id, name, 1, steps);
        var validation = MagicRecipeValidator.Validate(recipe);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        return recipe;
    }

    private static string FormatMagicRecipeStep(MagicRecipeStep step)
    {
        if (step.Kind == MagicRecipeStepKind.MagicAction) return "AI:" + step.Reference;
        var argument = step.Options is not null && step.Options.TryGetValue("argument", out var value) ? value : null;
        return "STEP:" + step.Reference + (string.IsNullOrWhiteSpace(argument) ? string.Empty : ":" + argument);
    }

    private void ClearAiContext_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.ContextStack)) return;
        Services.AiContext.Clear();
        AiContextStatusText.Text = "0 / 8 captures";
    }

    private async void AddSelectedHistoryToAiContext_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.ContextStack)) return;
        var added = 0;
        foreach (var display in ActiveSelectedHistoryDisplays())
        {
            if (await TryHistoryAssetAsync(display.Item) is { } asset && Services.AiContext.TryAdd(asset, display.Item.SourceKind)) added++;
        }
        AiContextStatusText.Text = $"{Services.AiContext.Count} / 8 captures";
        ShowStatus(added > 0 ? $"Added {added} capture(s) to AI Context Stack." : "No new captures were added to AI Context Stack.");
    }

    private async Task<CaptureAsset?> TryHistoryAssetAsync(HistoryItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Services.HistoryStore.GetAbsolutePath(item);
            if (!File.Exists(path)) return null;
            var bytes = await ImageFileReader.ReadAsync(path, cancellationToken);
            ImageWorkloadLimits.ValidateEncodedLength(bytes.LongLength);
            _ = Enum.TryParse<CaptureSourceKind>(item.SourceKind, out var kind);
            return new CaptureAsset(item.Id, item.CreatedUtc, new Magic.Capture.Core.Geometry.PixelRect(0, 0, item.Width, item.Height), bytes, item.Width, item.Height, kind, item.SourceDisplayName ?? item.SourceKind, item.WindowTitle, item.ProcessName, item.MonitorName, ExecutablePath: item.ExecutablePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Log.Error("HistoryAiContext", ex);
            return null;
        }
    }

    private void SemanticCompare_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProAi(ProductFeature.SemanticCompare)) return;
        if (_compareA is null || _compareB is null) { ShowStatus("Choose both comparison images first.", InfoBarSeverity.Warning); return; }
        using var aBitmap = BitmapCodec.Decode(_compareA);
        using var bBitmap = BitmapCodec.Decode(_compareB);
        var a = CaptureAsset.Create(new Magic.Capture.Core.Geometry.PixelRect(0, 0, aBitmap.Width, aBitmap.Height), _compareA, CaptureSourceKind.Compare, _compareAName ?? "Compare A");
        var b = CaptureAsset.Create(new Magic.Capture.Core.Geometry.PixelRect(0, 0, bBitmap.Width, bBitmap.Height), _compareB, CaptureSourceKind.Compare, _compareBName ?? "Compare B");
        Services.AiContext.Clear();
        Services.AiContext.TryAdd(b, "Compare B");
        var window = new MagicActionWindow(a, Services, "compare.semantic");
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    internal void ApplySettingsToUi(AppSettings settings, string? hotkeyError)
    {
        var themeItem = ThemeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, settings.Theme.ToString(), StringComparison.Ordinal));
        if (themeItem is not null) ThemeCombo.SelectedItem = themeItem;
        AutoCopyCheck.IsChecked = settings.AutoCopyImage;
        var overlayThemeItem = CaptureOverlayThemeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag as string, settings.CaptureOverlayTheme.ToString(), StringComparison.Ordinal));
        if (overlayThemeItem is not null) CaptureOverlayThemeCombo.SelectedItem = overlayThemeItem;
        CaptureCursorCheck.IsChecked = settings.CaptureCursor;
        var postItem = PostCaptureActionCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, settings.DefaultPostCaptureAction.ToString(), StringComparison.Ordinal));
        if (postItem is not null) PostCaptureActionCombo.SelectedItem = postItem;
        PinOpacityBox.Value = settings.PinOpacity * 100;
        HistoryEnabledCheck.IsChecked = settings.HistoryEnabled;
        HistoryDaysBox.Value = settings.HistoryMaximumAgeDays ?? 0;
        HistoryCountBox.Value = settings.HistoryMaximumCount ?? 0;
        FilenameTemplateBox.Text = settings.FileNameTemplate;
        JpegQualityBox.Value = settings.JpegQuality;
        RedactBeforeCopyCheck.IsChecked = settings.RedactBeforeCopy;
        RedactBeforeSaveCheck.IsChecked = settings.RedactBeforeSave;
        RedactBeforePinCheck.IsChecked = settings.RedactBeforePin;
        RedactBeforeWorkflowCheck.IsChecked = settings.RedactBeforeWorkflow;
        var redactionItem = RedactionStyleCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag as string, settings.OutboundRedactionStyle.ToString(), StringComparison.Ordinal));
        if (redactionItem is not null) RedactionStyleCombo.SelectedItem = redactionItem;
        SensitiveWordsBox.Text = string.Join(Environment.NewLine, settings.SensitiveWords);
        SensitivePatternsBox.Text = string.Join(Environment.NewLine, settings.SensitivePatterns.Select(pattern => $"{pattern.Label}={pattern.Pattern}"));
        HotkeyWin.IsChecked = settings.RegionHotkey.Modifiers.HasFlag(HotkeyModifiers.Windows);
        HotkeyCtrl.IsChecked = settings.RegionHotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        HotkeyAlt.IsChecked = settings.RegionHotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        HotkeyShift.IsChecked = settings.RegionHotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        HotkeyKey.Text = ((char)settings.RegionHotkey.VirtualKey).ToString().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(settings.PreferredOcrLanguage)) OcrLanguageCombo.SelectedItem = settings.PreferredOcrLanguage;
        var activeRegion = Services.Hotkeys.ActiveRegionHotkey;
        HotkeyStatusText.Text = activeRegion == settings.RegionHotkey
            ? $"Active · {FormatGesture(settings.RegionHotkey)}"
            : $"Configured · {FormatGesture(settings.RegionHotkey)} · not active";
        HotkeyErrorText.Text = hotkeyError ?? string.Empty;

        var selectedProfileId = (CaptureProfileCombo.SelectedItem as CaptureProfile)?.Id ?? settings.DefaultCaptureProfileId;
        CaptureProfileCombo.ItemsSource = settings.CaptureProfiles;
        CaptureProfileCombo.SelectedItem = settings.CaptureProfiles.FirstOrDefault(profile => string.Equals(profile.Id, selectedProfileId, StringComparison.Ordinal));
        if (CaptureProfileCombo.SelectedItem is null && settings.CaptureProfiles.Count > 0) CaptureProfileCombo.SelectedIndex = 0;
        RecentRegionCombo.ItemsSource = settings.RecentRegions.Select(region => new RecentRegionOption(region)).ToArray();
        if (settings.RecentRegions.Count > 0 && RecentRegionCombo.SelectedIndex < 0) RecentRegionCombo.SelectedIndex = 0;
        RefreshPersonalizationSettingsUi(settings);
    }

    private void RefreshPersonalizationSettingsUi(AppSettings settings)
    {
        PersonalHotkeyList.ItemsSource = settings.PersonalHotkeys.ToArray();
        ToolbarActionList.ItemsSource = settings.ToolbarActions.ToArray();
        OverlayActionList.ItemsSource = settings.OverlayActions.ToArray();
        AnnotationStylePresetList.ItemsSource = settings.AnnotationStylePresets.ToArray();
        MonitorPreferenceList.ItemsSource = settings.MonitorPreferences.ToArray();
        AppCaptureRuleList.ItemsSource = settings.AppCaptureRules.ToArray();
        DefaultAnnotationToolCombo.ItemsSource = Enum.GetValues<AnnotationKind>();
        DefaultAnnotationToolCombo.SelectedItem = settings.DefaultAnnotationTool;
        RememberLastAnnotationToolCheck.IsChecked = settings.RememberLastAnnotationTool;
        AppRuleProfileCombo.ItemsSource = settings.CaptureProfiles.ToArray();
        var actionOptions = new[] { "Inherit" }.Concat(Enum.GetNames<PostCaptureAction>()).ToArray();
        MonitorActionOverrideCombo.ItemsSource = actionOptions;
        AppRuleActionOverrideCombo.ItemsSource = actionOptions;
        if (MonitorActionOverrideCombo.SelectedIndex < 0) MonitorActionOverrideCombo.SelectedIndex = 0;
        if (AppRuleActionOverrideCombo.SelectedIndex < 0) AppRuleActionOverrideCombo.SelectedIndex = 0;
        if (string.IsNullOrWhiteSpace(PersonalHotkeyTargetBox.Text)) PersonalHotkeyTargetBox.Text = CaptureHotkeyAction.ActiveMonitor.ToString();
    }

    private static HotkeyGesture ParsePersonalHotkeyGesture(string? keyText, bool win, bool ctrl, bool alt, bool shift)
    {
        var text = (keyText ?? string.Empty).Trim().ToUpperInvariant();
        int virtualKey;
        if (text.Length == 1 && char.IsLetterOrDigit(text[0])) virtualKey = text[0];
        else if (text.StartsWith("F", StringComparison.Ordinal) && int.TryParse(text[1..], out var f) && f is >= 1 and <= 24) virtualKey = 0x6F + f;
        else throw new InvalidOperationException("Personal hotkey key must be A-Z, 0-9, or F1-F24.");
        var modifiers = HotkeyModifiers.None;
        if (win) modifiers |= HotkeyModifiers.Windows;
        if (ctrl) modifiers |= HotkeyModifiers.Control;
        if (alt) modifiers |= HotkeyModifiers.Alt;
        if (shift) modifiers |= HotkeyModifiers.Shift;
        var gesture = new HotkeyGesture(modifiers, virtualKey);
        if (!AppSettingsRules.IsValidHotkey(gesture)) throw new InvalidOperationException("Choose at least one modifier and a valid hotkey key.");
        return gesture;
    }

    private static string HotkeyKeyText(HotkeyGesture gesture) => gesture.VirtualKey is >= 0x70 and <= 0x87
        ? $"F{gesture.VirtualKey - 0x6F}"
        : ((char)gesture.VirtualKey).ToString();

    private void PersonalHotkeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PersonalHotkeyList.SelectedItem is not PersonalHotkeyBinding binding) return;
        PersonalHotkeyNameBox.Text = binding.Name;
        PersonalHotkeyTargetBox.Text = binding.Target;
        PersonalHotkeyEnabledCheck.IsChecked = binding.Enabled;
        PersonalHotkeyWinCheck.IsChecked = binding.Gesture.Modifiers.HasFlag(HotkeyModifiers.Windows);
        PersonalHotkeyCtrlCheck.IsChecked = binding.Gesture.Modifiers.HasFlag(HotkeyModifiers.Control);
        PersonalHotkeyAltCheck.IsChecked = binding.Gesture.Modifiers.HasFlag(HotkeyModifiers.Alt);
        PersonalHotkeyShiftCheck.IsChecked = binding.Gesture.Modifiers.HasFlag(HotkeyModifiers.Shift);
        PersonalHotkeyKeyBox.Text = HotkeyKeyText(binding.Gesture);
        PersonalHotkeyKindCombo.SelectedItem = PersonalHotkeyKindCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), binding.Kind.ToString(), StringComparison.Ordinal));
    }


    private async Task ValidatePersonalHotkeyTargetAsync(PersonalHotkeyKind kind, string target)
    {
        switch (kind)
        {
            case PersonalHotkeyKind.Capture:
                if (target.StartsWith("profile:", StringComparison.OrdinalIgnoreCase))
                {
                    var profileId = target["profile:".Length..];
                    if (!Services.Settings.CaptureProfiles.Any(item => string.Equals(item.Id, profileId, StringComparison.Ordinal)))
                        throw new InvalidOperationException("Capture hotkey profile target must be an existing profile ID.");
                }
                else if (!Enum.TryParse<CaptureHotkeyAction>(target, true, out _))
                    throw new InvalidOperationException("Capture hotkey target must be a supported capture action or profile:<profile-id>.");
                break;
            case PersonalHotkeyKind.Workflow:
                if (!(await Services.Workflows.LoadAsync()).Any(item => string.Equals(item.Id, target, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Workflow hotkey target must be an existing workflow ID.");
                break;
            case PersonalHotkeyKind.MagicAction:
                var customActions = await Services.MagicActionStore.LoadAsync();
                if (!BuiltInMagicActions.All.Concat(customActions).Any(item => string.Equals(item.Id, target, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Magic Action hotkey target must be an existing action ID.");
                break;
            case PersonalHotkeyKind.Editor:
                if (!string.Equals(target, "open-last", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(target, "open", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Editor hotkey target must be open-last.");
                break;
        }
    }

    private async void SavePersonalHotkey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = PersonalHotkeyNameBox.Text?.Trim();
            var target = PersonalHotkeyTargetBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Hotkey name and target are required.");
            var kindText = (PersonalHotkeyKindCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!Enum.TryParse<PersonalHotkeyKind>(kindText, out var kind)) throw new InvalidOperationException("Choose a hotkey kind.");
            await ValidatePersonalHotkeyTargetAsync(kind, target);
            var gesture = ParsePersonalHotkeyGesture(PersonalHotkeyKeyBox.Text,
                PersonalHotkeyWinCheck.IsChecked == true, PersonalHotkeyCtrlCheck.IsChecked == true,
                PersonalHotkeyAltCheck.IsChecked == true, PersonalHotkeyShiftCheck.IsChecked == true);
            var selected = PersonalHotkeyList.SelectedItem as PersonalHotkeyBinding;
            var binding = new PersonalHotkeyBinding(selected?.Id ?? Guid.NewGuid().ToString("N"), name, kind, target, gesture, PersonalHotkeyEnabledCheck.IsChecked == true);
            await ((App)Application.Current).MutateSettingsAsync(current =>
            {
                var proposed = AppSettingsRules.NormalizeForRuntime(current with
                {
                    PersonalHotkeys = current.PersonalHotkeys.Where(item => item.Id != binding.Id).Append(binding).ToArray()
                });
                if (!proposed.PersonalHotkeys.Any(item => item.Id == binding.Id))
                    throw new InvalidOperationException("The binding conflicts with another hotkey or has an invalid target.");
                return proposed;
            }, SettingsRuntimeEffects.MainWindowUi);
            SettingsStatusText.Text = "Personal hotkey saved.";
        }
        catch (Exception ex) { SettingsStatusText.Text = ex.Message; }
    }

    private async void DeletePersonalHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (PersonalHotkeyList.SelectedItem is not PersonalHotkeyBinding binding) return;
        await ((App)Application.Current).MutateSettingsAsync(
            current => current with { PersonalHotkeys = current.PersonalHotkeys.Where(item => item.Id != binding.Id).ToArray() },
            SettingsRuntimeEffects.MainWindowUi);
        SettingsStatusText.Text = "Personal hotkey deleted.";
    }

    private async Task MovePersonalizationActionAsync(bool toolbar, int delta, bool toggle)
    {
        var selected = toolbar ? ToolbarActionList.SelectedItem as PersonalizationActionItem : OverlayActionList.SelectedItem as PersonalizationActionItem;
        if (selected is null) return;
        await ((App)Application.Current).MutateSettingsAsync(current =>
        {
            var source = (toolbar ? current.ToolbarActions : current.OverlayActions).ToList();
            var index = source.FindIndex(item => item.Id == selected.Id);
            if (index < 0) return current;
            if (toggle) source[index] = source[index] with { Visible = !source[index].Visible };
            else
            {
                var destination = Math.Clamp(index + delta, 0, source.Count - 1);
                if (destination == index) return current;
                var item = source[index]; source.RemoveAt(index); source.Insert(destination, item);
            }
            return toolbar ? current with { ToolbarActions = source } : current with { OverlayActions = source };
        }, SettingsRuntimeEffects.MainWindowUi);
    }

    private async void MoveToolbarActionUp_Click(object sender, RoutedEventArgs e) => await MovePersonalizationActionAsync(true, -1, false);
    private async void MoveToolbarActionDown_Click(object sender, RoutedEventArgs e) => await MovePersonalizationActionAsync(true, 1, false);
    private async void ToggleToolbarAction_Click(object sender, RoutedEventArgs e) => await MovePersonalizationActionAsync(true, 0, true);
    private async void MoveOverlayActionUp_Click(object sender, RoutedEventArgs e) => await MovePersonalizationActionAsync(false, -1, false);
    private async void MoveOverlayActionDown_Click(object sender, RoutedEventArgs e) => await MovePersonalizationActionAsync(false, 1, false);
    private async void ToggleOverlayAction_Click(object sender, RoutedEventArgs e) => await MovePersonalizationActionAsync(false, 0, true);

    private async void DeleteAnnotationStylePreset_Click(object sender, RoutedEventArgs e)
    {
        if (AnnotationStylePresetList.SelectedItem is not AnnotationStylePreset preset) return;
        await ((App)Application.Current).MutateSettingsAsync(
            current => current with { AnnotationStylePresets = current.AnnotationStylePresets.Where(item => item.Id != preset.Id).ToArray() },
            SettingsRuntimeEffects.MainWindowUi);
    }

    private static bool? ParseBooleanOverride(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "On" => true,
        "Off" => false,
        _ => null
    };

    private static PostCaptureAction? ParsePostCaptureOverride(ComboBox combo)
    {
        var text = combo.SelectedItem?.ToString();
        return Enum.TryParse<PostCaptureAction>(text, out var action) ? action : null;
    }

    private static void SelectOverride(ComboBox combo, bool? value)
    {
        var tag = value is true ? "On" : value is false ? "Off" : "Inherit";
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private void MonitorPreferenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorPreferenceList.SelectedItem is not MonitorCapturePreference preference) return;
        MonitorDeviceNameBox.Text = preference.DeviceName;
        SelectOverride(MonitorCursorOverrideCombo, preference.CaptureCursor);
        MonitorActionOverrideCombo.SelectedItem = preference.PostCaptureAction?.ToString() ?? "Inherit";
    }

    private async void SaveMonitorPreference_Click(object sender, RoutedEventArgs e)
    {
        var device = MonitorDeviceNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(device)) { SettingsStatusText.Text = "Monitor device name is required."; return; }
        var preference = new MonitorCapturePreference(device, ParseBooleanOverride(MonitorCursorOverrideCombo), ParsePostCaptureOverride(MonitorActionOverrideCombo));
        await ((App)Application.Current).MutateSettingsAsync(
            current => current with
            {
                MonitorPreferences = current.MonitorPreferences.Where(item => !string.Equals(item.DeviceName, device, StringComparison.OrdinalIgnoreCase)).Append(preference).ToArray()
            },
            SettingsRuntimeEffects.MainWindowUi);
        SettingsStatusText.Text = "Monitor preference saved.";
    }

    private async void DeleteMonitorPreference_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorPreferenceList.SelectedItem is not MonitorCapturePreference preference) return;
        await ((App)Application.Current).MutateSettingsAsync(
            current => current with
            {
                MonitorPreferences = current.MonitorPreferences.Where(item => !string.Equals(item.DeviceName, preference.DeviceName, StringComparison.OrdinalIgnoreCase)).ToArray()
            },
            SettingsRuntimeEffects.MainWindowUi);
    }

    private void AppCaptureRuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppCaptureRuleList.SelectedItem is not AppCaptureRule rule) return;
        AppRuleExecutableBox.Text = rule.ExecutableName;
        AppRuleEnabledCheck.IsChecked = rule.Enabled;
        AppRuleProfileCombo.SelectedItem = Services.Settings.CaptureProfiles.FirstOrDefault(item => item.Id == rule.CaptureProfileId);
        SelectOverride(AppRuleCursorOverrideCombo, rule.CaptureCursor);
        AppRuleActionOverrideCombo.SelectedItem = rule.PostCaptureAction?.ToString() ?? "Inherit";
    }

    private async void SaveAppCaptureRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = AppRuleExecutableBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || executable.IndexOfAny(['\\', '/', ':']) >= 0)
                throw new InvalidOperationException("App rule requires an executable file name such as app.exe, not a path.");
            if (AppRuleProfileCombo.SelectedItem is not CaptureProfile profile) throw new InvalidOperationException("Choose a capture profile for the app rule.");
            var selected = AppCaptureRuleList.SelectedItem as AppCaptureRule;
            var rule = new AppCaptureRule(selected?.Id ?? Guid.NewGuid().ToString("N"), executable, profile.Id,
                AppRuleEnabledCheck.IsChecked == true, ParseBooleanOverride(AppRuleCursorOverrideCombo), ParsePostCaptureOverride(AppRuleActionOverrideCombo));
            await ((App)Application.Current).MutateSettingsAsync(current =>
            {
                var proposed = AppSettingsRules.NormalizeForRuntime(current with
                {
                    AppCaptureRules = current.AppCaptureRules
                        .Where(item => item.Id != rule.Id && !string.Equals(item.ExecutableName, executable, StringComparison.OrdinalIgnoreCase))
                        .Append(rule).ToArray()
                });
                if (!proposed.AppCaptureRules.Any(item => item.Id == rule.Id))
                    throw new InvalidOperationException("The app rule is invalid or duplicates an existing executable rule.");
                return proposed;
            }, SettingsRuntimeEffects.MainWindowUi);
            SettingsStatusText.Text = "App capture rule saved.";
        }
        catch (Exception ex) { SettingsStatusText.Text = ex.Message; }
    }

    private async void DeleteAppCaptureRule_Click(object sender, RoutedEventArgs e)
    {
        if (AppCaptureRuleList.SelectedItem is not AppCaptureRule rule) return;
        await ((App)Application.Current).MutateSettingsAsync(
            current => current with { AppCaptureRules = current.AppCaptureRules.Where(item => item.Id != rule.Id).ToArray() },
            SettingsRuntimeEffects.MainWindowUi);
    }

    private async Task ResetSettingsSectionAsync(SettingsSection section)
    {
        var effects = section == SettingsSection.History
            ? SettingsRuntimeEffects.MainWindowUi | SettingsRuntimeEffects.HistoryRetention
            : SettingsRuntimeEffects.MainWindowUi;
        await ((App)Application.Current).MutateSettingsAsync(current => AppSettingsRules.ResetSection(current, section), effects);
        SettingsStatusText.Text = $"{section} settings reset.";
    }

    private async void ResetHotkeysSection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.Hotkeys);
    private async void ResetCaptureSection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.Capture);
    private async void ResetOutputSection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.Output);
    private async void ResetPrivacySection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.Privacy);
    private async void ResetHistorySection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.History);
    private async void ResetPersonalizationSection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.Personalization);
    private async void ResetContextPreferencesSection_Click(object sender, RoutedEventArgs e) => await ResetSettingsSectionAsync(SettingsSection.ContextPreferences);

    internal void ApplyStartupState(StartupState state)
    {
        _updatingStartupToggle = true;
        StartWithWindowsToggle.IsOn = state.IsEnabled;
        StartWithWindowsToggle.IsEnabled = state.IsAvailable;
        StartupStatusText.Text = state.Description;
        _updatingStartupToggle = false;
    }

    private async void StartWithWindows_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingStartupToggle || _services is null) return;
        var state = await Services.Startup.SetEnabledAsync(StartWithWindowsToggle.IsOn);
        ApplyStartupState(state);
    }

    private async Task RefreshStorePriceAsync()
    {
        if (_services is null) return;
        if (Services.Entitlements.Current.IsPro)
        {
            ProPriceText.Text = "Owned — lifetime";
            return;
        }

        var quote = await Services.StorePurchase.QueryProPriceAsync();
        if (!quote.Available || string.IsNullOrWhiteSpace(quote.FormattedPrice))
        {
            ProPriceText.Text = "Price shown by Microsoft Store";
            return;
        }

        if (quote.IsOnSale && !string.IsNullOrWhiteSpace(quote.FormattedBasePrice) &&
            !string.Equals(quote.FormattedPrice, quote.FormattedBasePrice, StringComparison.Ordinal))
        {
            var saleSuffix = quote.SaleEndDate is { } saleEnd
                ? $" · sale ends {saleEnd.ToLocalTime():d}"
                : string.Empty;
            ProPriceText.Text = $"{quote.FormattedPrice} now · regular {quote.FormattedBasePrice}{saleSuffix}";
            UpgradeButton.Content = $"Upgrade to Pro — {quote.FormattedPrice}";
            HomeUpgradeButton.Content = $"Upgrade to Pro — {quote.FormattedPrice}";
        }
        else
        {
            ProPriceText.Text = $"Lifetime · {quote.FormattedPrice}";
            UpgradeButton.Content = $"Upgrade to Pro — {quote.FormattedPrice}";
            HomeUpgradeButton.Content = $"Upgrade to Pro — {quote.FormattedPrice}";
        }
    }

    internal void ApplyEntitlementToUi(EntitlementSnapshot snapshot)
    {
        _ = RefreshStorePriceAsync();
        switch (snapshot.Tier)
        {
            case ProductTier.ProLifetime:
                PlanStatusText.Text = "Plan: Magic Capture Desktop Pro";
                TrialStatusText.Text = "Lifetime license";
                PlanPageTierText.Text = "Magic Capture Desktop Pro — Lifetime";
                PlanPageDetailText.Text = "Pro is unlocked through Microsoft Store. All current product features are available.";
                HomeUpgradeButton.Visibility = Visibility.Collapsed;
                UpgradeButton.Visibility = Visibility.Collapsed;
                break;
            case ProductTier.PlusTrial:
                var remaining = snapshot.TrialRemaining;
                var remainingText = remaining.TotalDays >= 1 ? $"{Math.Ceiling(remaining.TotalDays)} day(s) remaining" : $"{Math.Max(1, Math.Ceiling(remaining.TotalHours))} hour(s) remaining";
                PlanStatusText.Text = "Plan: 7-day Plus trial";
                TrialStatusText.Text = remainingText + " · Pro features remain separately labeled PRO";
                PlanPageTierText.Text = "Magic Capture Desktop Plus Trial";
                PlanPageDetailText.Text = $"Plus trial: {remainingText}. Plus is not sold and never auto-renews. Upgrade to Pro once for permanent access to Plus and Pro features.";
                HomeUpgradeButton.Visibility = Visibility.Visible;
                UpgradeButton.Visibility = Visibility.Visible;
                break;
            default:
                PlanStatusText.Text = "Plan: Magic Capture Desktop Free";
                TrialStatusText.Text = "Free forever";
                PlanPageTierText.Text = "Magic Capture Desktop Free";
                PlanPageDetailText.Text = "Your Plus trial has ended or is unavailable. Free remains usable forever. Pro is a one-time lifetime upgrade.";
                HomeUpgradeButton.Visibility = Visibility.Visible;
                UpgradeButton.Visibility = Visibility.Visible;
                break;
        }
        ApplyAiEntitlement();
    }

    internal async Task ShowTrialExpiredAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Your 7-day Plus trial has ended",
            Content = new TextBlock
            {
                Text = "Magic Capture Desktop remains free forever. Your captures and settings are untouched. Plus is not sold and nothing will be charged automatically. You can continue with Free or unlock Pro Lifetime through Microsoft Store.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            },
            PrimaryButtonText = "Upgrade to Pro",
            CloseButtonText = "Continue with Free",
            XamlRoot = Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        await Services.Entitlements.MarkTrialExpiryNoticeShownAsync();
        if (result == ContentDialogResult.Primary) await PurchaseProFromUiAsync();
    }

    private async void UpgradeToPro_Click(object sender, RoutedEventArgs e) => await PurchaseProFromUiAsync();

    private async Task PurchaseProFromUiAsync()
    {
        PurchaseStatusText.Text = "Opening Microsoft Store checkout…";
        var outcome = await ((App)Application.Current).PurchaseProAsync();
        PurchaseStatusText.Text = outcome.Message;
        if (outcome.Succeeded) await RefreshStorePriceAsync();
        ShowStatus(outcome.Message, outcome.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var keyText = (HotkeyKey.Text ?? "X").Trim().ToUpperInvariant();
            if (keyText.Length != 1 || !char.IsLetterOrDigit(keyText[0])) throw new InvalidOperationException("Hotkey key must be one letter or digit.");

            var modifiers = HotkeyModifiers.None;
            if (HotkeyWin.IsChecked == true) modifiers |= HotkeyModifiers.Windows;
            if (HotkeyCtrl.IsChecked == true) modifiers |= HotkeyModifiers.Control;
            if (HotkeyAlt.IsChecked == true) modifiers |= HotkeyModifiers.Alt;
            if (HotkeyShift.IsChecked == true) modifiers |= HotkeyModifiers.Shift;
            if (modifiers == HotkeyModifiers.None) throw new InvalidOperationException("Choose at least one hotkey modifier.");

            var proHistory = Services.Entitlements.CanUse(ProductFeature.UnlimitedHistory);
            var age = HistoryDaysBox.Value <= 0 ? (proHistory ? null : 30) : (int?)HistoryDaysBox.Value;
            var count = HistoryCountBox.Value <= 0 ? (proHistory ? null : 500) : (int?)HistoryCountBox.Value;
            if (!proHistory && (HistoryDaysBox.Value <= 0 || HistoryCountBox.Value <= 0))
                ShowStatus("Unlimited history retention is a Pro feature. Free/Plus limits were kept.", InfoBarSeverity.Informational);

            Func<AppSettings, AppSettings> settingsMutation = current => current with
            {
                KeepResident = true,
                Theme = Enum.TryParse<AppTheme>((ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var appTheme) ? appTheme : AppTheme.System,
                RegionHotkey = new HotkeyGesture(modifiers, keyText[0]),
                AutoCopyImage = AutoCopyCheck.IsChecked == true,
                CaptureOverlayTheme = Enum.TryParse<CaptureOverlayTheme>((CaptureOverlayThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var overlayTheme)
                    ? overlayTheme
                    : CaptureOverlayTheme.Dark,
                CaptureCursor = CaptureCursorCheck.IsChecked == true,
                DefaultPostCaptureAction = Enum.TryParse<PostCaptureAction>((PostCaptureActionCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var postAction) ? postAction : PostCaptureAction.ResultWindow,
                PinOpacity = Math.Clamp(PinOpacityBox.Value / 100d, 0.5, 1.0),
                HistoryEnabled = HistoryEnabledCheck.IsChecked == true,
                HistoryMaximumAgeDays = age,
                HistoryMaximumCount = count,
                FileNameTemplate = string.IsNullOrWhiteSpace(FilenameTemplateBox.Text) ? new AppSettings().FileNameTemplate : FilenameTemplateBox.Text.Trim(),
                JpegQuality = (int)Math.Clamp(JpegQualityBox.Value, 1, 100),
                PreferredOcrLanguage = OcrLanguageCombo.SelectedItem as string,
                RedactBeforeCopy = RedactBeforeCopyCheck.IsChecked == true,
                RedactBeforeSave = RedactBeforeSaveCheck.IsChecked == true,
                RedactBeforePin = RedactBeforePinCheck.IsChecked == true,
                RedactBeforeWorkflow = RedactBeforeWorkflowCheck.IsChecked == true,
                OutboundRedactionStyle = Enum.TryParse<RedactionStyle>((RedactionStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var redactionStyle)
                    ? redactionStyle
                    : RedactionStyle.Pixelate,
                SensitiveWords = ParseSensitiveWords(SensitiveWordsBox.Text),
                SensitivePatterns = ParseSensitivePatterns(SensitivePatternsBox.Text),
                DefaultAnnotationTool = DefaultAnnotationToolCombo.SelectedItem is AnnotationKind selectedTool ? selectedTool : AnnotationKind.Rectangle,
                RememberLastAnnotationTool = RememberLastAnnotationToolCheck.IsChecked == true
            };
            var resetPersistence = false;
            if (!Services.SettingsStore.IsPersistenceHealthy)
            {
                var recoveryDialog = new ContentDialog
                {
                    Title = "Settings storage is in recovery mode",
                    Content = "Magic Capture Desktop could not safely read the existing settings file. Replacing it will preserve the current primary/backup files as recovery copies before writing these settings.",
                    PrimaryButtonText = "Preserve old files and reset",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                if (await recoveryDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    SettingsStatusText.Text = "Settings were not overwritten.";
                    return;
                }
                resetPersistence = true;
            }
            await ((App)Application.Current).MutateSettingsAsync(
                settingsMutation,
                SettingsRuntimeEffects.All,
                resetPersistence: resetPersistence);
            SettingsStatusText.Text = Services.Hotkeys.LastRegistrationError ?? Services.Hotkeys.LastRollbackError ?? "Settings saved.";
        }
        catch (Exception ex) { SettingsStatusText.Text = ex.Message; }
    }

    private static IReadOnlyList<string> ParseSensitiveWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(AppSettingsRules.MaximumSensitiveWords)
            .ToArray();
    }

    private static IReadOnlyList<SensitivePattern> ParseSensitivePatterns(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<SensitivePattern>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lineNumber++;
            var separator = rawLine.IndexOf('=');
            if (separator <= 0 || separator == rawLine.Length - 1)
                throw new InvalidOperationException($"Sensitive pattern line {lineNumber} must use Label=regular-expression.");

            var label = rawLine[..separator].Trim();
            var pattern = rawLine[(separator + 1)..].Trim();
            if (label.Length > AppSettingsRules.MaximumSensitivePatternLabelLength)
                throw new InvalidOperationException($"Sensitive pattern label on line {lineNumber} is too long.");
            if (pattern.Length > AppSettingsRules.MaximumSensitivePatternLength)
                throw new InvalidOperationException($"Sensitive pattern on line {lineNumber} is too long.");
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern,
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Sensitive pattern line {lineNumber} is not a valid regular expression: {ex.Message}");
            }

            result.Add(new SensitivePattern(label, pattern));
            if (result.Count == AppSettingsRules.MaximumSensitivePatterns) break;
        }
        return result;
    }

    private static string FormatGesture(HotkeyGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(((char)gesture.VirtualKey).ToString().ToUpperInvariant());
        return string.Join(" + ", parts);
    }
}
