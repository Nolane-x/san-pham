using Magic.Capture.App.Documentation;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Platform;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Magic.Capture.App.Views;

public sealed partial class DocumentationWindow : Window
{
    private sealed record StepListItem(int Number, DocumentationStep Step)
    {
        public string DisplayTitle => $"{Number}. {(string.IsNullOrWhiteSpace(Step.Title) ? $"Step {Number}" : Step.Title)}";
        public string DisplayDescription => string.IsNullOrWhiteSpace(Step.Description) ? "No instruction yet." : Step.Description!;
    }

    private readonly ApplicationServices _services;
    private DocumentationProject _project = DocumentationProject.Create("Magic Capture guide");
    private Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private byte[]? _logoPng;
    private string? _projectPath;
    private bool _ownsRecording;
    private bool _busy;
    private Guid _recoverySessionId = Guid.NewGuid();
    private long _dirtyRevision;
    private long _lastRecoveryRevision;
    private string? _currentProjectDisplayName;
    private bool _suppressRecoveryAutosave;
    private bool _closingCleanly;
    private bool _closeCleanupComplete;
    private long _recoveryGeneration;
    private readonly DispatcherQueueTimer _recoveryTimer;
    private readonly SemaphoreSlim _recoveryWriteGate = new(1, 1);
    private readonly AppWindow _appWindow;

    internal DocumentationWindow(ApplicationServices services)
    {
        InitializeComponent();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _services.StepRecorder.StepCaptured += StepRecorder_StepCaptured;
        _services.StepRecorder.CaptureFailed += StepRecorder_CaptureFailed;
        Closed += DocumentationWindow_Closed;
        WindowHelpers.MoveAndResize(this, 110, 70, 1320, 860);
        _appWindow = WindowHelpers.GetAppWindow(this);
        _recoveryTimer = DispatcherQueue.CreateTimer();
        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _recoveryTimer.IsRepeating = false;
        _recoveryTimer.Tick += RecoveryTimer_Tick;
        _appWindow.Closing += DocumentationAppWindow_Closing;
        InitializeTemplatePicker();
        LoadProjectMetadata();
        RefreshStepList();
        ProjectTitleBox.TextChanged += ProjectMetadata_TextChanged;
        ProjectSubtitleBox.TextChanged += ProjectMetadata_TextChanged;
        ProjectHeaderBox.TextChanged += ProjectMetadata_TextChanged;
        ProjectFooterBox.TextChanged += ProjectMetadata_TextChanged;
        TemplateComboBox.SelectionChanged += ProjectTemplate_SelectionChanged;
    }

    internal DocumentationWindow(
        DocumentationProjectPackage package,
        ApplicationServices services,
        Guid recoverySessionId,
        long dirtyRevision,
        string? displayName)
        : this(services)
    {
        ArgumentNullException.ThrowIfNull(package);
        _suppressRecoveryAutosave = true;
        try
        {
            _project = package.Project;
            _images = package.Images.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _logoPng = package.LogoPng;
            _projectPath = null;
            _recoverySessionId = recoverySessionId == Guid.Empty ? Guid.NewGuid() : recoverySessionId;
            _dirtyRevision = Math.Max(1, dirtyRevision);
            _lastRecoveryRevision = _dirtyRevision;
            _currentProjectDisplayName = displayName;
            LoadProjectMetadata();
            RefreshStepList();
            ShowStatus("Recovered local documentation autosave. Save it to choose a destination; no existing project file was overwritten.", InfoBarSeverity.Success);
        }
        finally
        {
            _suppressRecoveryAutosave = false;
        }
    }

    private async void StartStopRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_ownsRecording)
        {
            await StopRecordingAsync("Recording stopped. Review and edit the captured steps before export.");
            return;
        }
        if (_services.StepRecorder.IsRunning)
        {
            ShowStatus("Another Documentation Builder window already owns the active Step Recorder session.", InfoBarSeverity.Warning);
            return;
        }
        if (_project.Steps.Count >= DocumentationPolicy.MaximumSteps)
        {
            ShowStatus($"This guide already contains the maximum {DocumentationPolicy.MaximumSteps} steps.", InfoBarSeverity.Warning);
            return;
        }
        try
        {
            _services.StepRecorder.Start();
            _ownsRecording = true;
            StartStopButton.Content = "Stop recording";
            ShowStatus("Recording. Click controls or use safe shortcuts; plain typed characters are never retained.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationRecorderStart", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void StepRecorder_StepCaptured(object? sender, DocumentationStepAsset asset)
    {
        if (!_ownsRecording) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_ownsRecording || _project.Steps.Count >= DocumentationPolicy.MaximumSteps) return;
            try
            {
                var steps = _project.Steps.ToList();
                steps.Add(asset.Step);
                _images[asset.Step.ImageKey] = asset.PngBytes;
                if (steps.Count == 1 && string.Equals(ProjectTitleBox.Text?.Trim(), "Magic Capture guide", StringComparison.Ordinal))
                    ProjectTitleBox.Text = DocumentationPolicy.GenerateProjectTitle(asset.Step.Target);
                _project = SyncProjectMetadata(_project with { Steps = steps.ToArray(), ModifiedUtc = DateTimeOffset.UtcNow });
                RefreshStepList(asset.Step.Id);
                ScheduleRecoveryAutosave();
                ShowStatus($"Captured step {steps.Count}: {asset.Step.Title ?? "Untitled step"}.", InfoBarSeverity.Success);
            }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
            {
                _services.Log.Error("DocumentationStepCaptured", ex);
                ShowStatus(ex.Message, InfoBarSeverity.Error);
            }
        });
    }

    private void StepRecorder_CaptureFailed(object? sender, string message)
    {
        if (!_ownsRecording) return;
        DispatcherQueue.TryEnqueue(() => ShowStatus("A step could not be captured: " + message, InfoBarSeverity.Warning));
    }

    private async void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            SetBusy(true, "Adding image…");
            var source = await ImageFileReader.ReadAsync(file.Path);
            byte[] png;
            using (var bitmap = BitmapCodec.DecodeForPixelProcessing(source)) png = BitmapCodec.EncodePng(bitmap);
            if (!PngDimensions.TryRead(png, out var width, out var height)) throw new InvalidDataException("The selected image could not be decoded.");
            if (width > DocumentationPolicy.MaximumCaptureWidth || height > DocumentationPolicy.MaximumCaptureHeight)
            {
                var scale = Math.Min(DocumentationPolicy.MaximumCaptureWidth / (double)width, DocumentationPolicy.MaximumCaptureHeight / (double)height);
                width = Math.Max(1, (int)Math.Round(width * scale));
                height = Math.Max(1, (int)Math.Round(height * scale));
                png = _services.Transforms.Resize(png, width, height);
            }
            DocumentationArchivePolicy.ValidateImageLength(png.LongLength);
            var id = Guid.NewGuid().ToString("N");
            var key = $"steps/{id}.png";
            var step = DocumentationPolicy.NormalizeStep(new DocumentationStep(
                id,
                DateTimeOffset.UtcNow,
                key,
                width,
                height,
                null,
                null,
                null,
                null,
                Path.GetFileNameWithoutExtension(file.Name),
                "Describe what the reader should do in this step.",
                null));
            var steps = _project.Steps.ToList();
            if (steps.Count >= DocumentationPolicy.MaximumSteps) throw new InvalidOperationException($"A guide can contain at most {DocumentationPolicy.MaximumSteps} steps.");
            steps.Add(step);
            _images[key] = png;
            _project = SyncProjectMetadata(_project with { Steps = steps.ToArray() });
            RefreshStepList(step.Id);
            ScheduleRecoveryAutosave();
            ShowStatus($"Added {file.Name} as step {steps.Count}.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationAddImage", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private async void ChooseLogo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            SetBusy(true, "Preparing logo…");
            var source = await ImageFileReader.ReadAsync(file.Path);
            byte[] png;
            using (var bitmap = BitmapCodec.DecodeForPixelProcessing(source)) png = BitmapCodec.EncodePng(bitmap);
            if (!PngDimensions.TryRead(png, out var width, out var height)) throw new InvalidDataException("The selected logo could not be decoded.");
            if (width > DocumentationPolicy.MaximumLogoWidth || height > DocumentationPolicy.MaximumLogoHeight)
            {
                var scale = Math.Min(DocumentationPolicy.MaximumLogoWidth / (double)width, DocumentationPolicy.MaximumLogoHeight / (double)height);
                width = Math.Max(1, (int)Math.Round(width * scale));
                height = Math.Max(1, (int)Math.Round(height * scale));
                png = _services.Transforms.Resize(png, width, height);
            }
            DocumentationArchivePolicy.ValidateImageLength(png.LongLength);
            _logoPng = png;
            _project = SyncProjectMetadata(_project with { LogoImageKey = "logo.png" });
            UpdateLogoStatus(file.Name);
            ScheduleRecoveryAutosave();
            ShowStatus("Logo embedded locally in this documentation project.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationChooseLogo", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private void ClearLogo_Click(object sender, RoutedEventArgs e)
    {
        _logoPng = null;
        _project = SyncProjectMetadata(_project with { LogoImageKey = null });
        UpdateLogoStatus();
        ScheduleRecoveryAutosave();
        ShowStatus("Logo removed from the guide.", InfoBarSeverity.Informational);
    }

    private void ApplyStep_Click(object sender, RoutedEventArgs e)
    {
        if (StepList.SelectedItem is not StepListItem selected) return;
        var steps = _project.Steps.ToArray();
        var index = Array.FindIndex(steps, step => string.Equals(step.Id, selected.Step.Id, StringComparison.Ordinal));
        if (index < 0) return;
        steps[index] = DocumentationPolicy.NormalizeStep(steps[index] with
        {
            Title = NullIfWhiteSpace(StepTitleBox.Text),
            Description = NullIfWhiteSpace(StepDescriptionBox.Text),
            Section = NullIfWhiteSpace(StepSectionBox.Text)
        });
        _project = SyncProjectMetadata(_project with { Steps = steps, ModifiedUtc = DateTimeOffset.UtcNow });
        RefreshStepList(steps[index].Id);
        ScheduleRecoveryAutosave();
        ShowStatus("Selected step updated.", InfoBarSeverity.Success);
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e) => MoveSelectedStep(-1);
    private void MoveStepDown_Click(object sender, RoutedEventArgs e) => MoveSelectedStep(1);

    private void StepList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_busy)
        {
            RefreshStepList();
            return;
        }
        var selectedId = (StepList.SelectedItem as StepListItem)?.Step.Id;
        var ordered = StepList.Items.OfType<StepListItem>().Select(item => item.Step).ToArray();
        if (ordered.Length != _project.Steps.Count || ordered.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
        {
            RefreshStepList(selectedId);
            ShowStatus("The drag reorder could not be applied safely; the previous order was restored.", InfoBarSeverity.Warning);
            return;
        }
        var expectedIds = _project.Steps.Select(step => step.Id).ToHashSet(StringComparer.Ordinal);
        if (ordered.Any(step => !expectedIds.Contains(step.Id)))
        {
            RefreshStepList(selectedId);
            ShowStatus("The drag reorder contained an unknown step and was ignored.", InfoBarSeverity.Warning);
            return;
        }
        _project = SyncProjectMetadata(_project with { Steps = ordered, ModifiedUtc = DateTimeOffset.UtcNow });
        RefreshStepList(selectedId);
        ScheduleRecoveryAutosave();
        ShowStatus("Step order updated by drag and drop.", InfoBarSeverity.Success);
    }

    private void MoveSelectedStep(int delta)
    {
        if (StepList.SelectedItem is not StepListItem selected) return;
        _project = DocumentationPolicy.MoveStep(SyncProjectMetadata(_project), selected.Step.Id, delta);
        RefreshStepList(selected.Step.Id);
        ScheduleRecoveryAutosave();
    }

    private void DuplicateStep_Click(object sender, RoutedEventArgs e)
    {
        if (StepList.SelectedItem is not StepListItem selected) return;
        try
        {
            var newId = Guid.NewGuid().ToString("N");
            _project = DocumentationPolicy.DuplicateStep(SyncProjectMetadata(_project), selected.Step.Id, newId);
            RefreshStepList(newId);
            ScheduleRecoveryAutosave();
            ShowStatus("Step duplicated. The duplicate safely reuses the same local image asset.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowStatus(ex.Message, InfoBarSeverity.Warning); }
    }

    private void MergeNext_Click(object sender, RoutedEventArgs e)
    {
        if (StepList.SelectedItem is not StepListItem selected) return;
        var index = _project.Steps.ToList().FindIndex(step => step.Id == selected.Step.Id);
        if (index < 0 || index >= _project.Steps.Count - 1)
        {
            ShowStatus("Select a step that has a following step to merge.", InfoBarSeverity.Warning);
            return;
        }
        var next = _project.Steps[index + 1];
        _project = DocumentationPolicy.MergeSteps(SyncProjectMetadata(_project), selected.Step.Id, next.Id);
        PruneUnreferencedImages();
        RefreshStepList(selected.Step.Id);
        ScheduleRecoveryAutosave();
        ShowStatus("Merged the selected step with the following step.", InfoBarSeverity.Success);
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (StepList.SelectedItem is not StepListItem selected) return;
        _project = DocumentationPolicy.RemoveStep(SyncProjectMetadata(_project), selected.Step.Id);
        PruneUnreferencedImages();
        RefreshStepList();
        ScheduleRecoveryAutosave();
        ShowStatus("Step removed.", InfoBarSeverity.Success);
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".magicdoc");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            if (_ownsRecording) await StopRecordingAsync("Recording stopped to open a project.");
            SetBusy(true, "Opening documentation project…");
            var package = await _services.DocumentationProjects.LoadAsync(file.Path);
            _suppressRecoveryAutosave = true;
            try
            {
                await InvalidateAndDeleteRecoveryAsync();
                _project = package.Project;
                _images = package.Images.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                _logoPng = package.LogoPng;
                _projectPath = file.Path;
                _currentProjectDisplayName = file.Name;
                _recoverySessionId = Guid.NewGuid();
                _dirtyRevision = 0;
                _lastRecoveryRevision = 0;
                LoadProjectMetadata();
                RefreshStepList();
            }
            finally
            {
                _suppressRecoveryAutosave = false;
            }
            ShowStatus($"Opened {file.Name} with {_project.Steps.Count} step(s).", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationOpen", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            SetBusy(true, "Saving documentation project…");
            _project = SyncProjectMetadata(_project);
            var savedRevision = _dirtyRevision;
            var savedSessionId = _recoverySessionId;
            var savedGeneration = _recoveryGeneration;
            var path = _projectPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = SanitizeFileName(_project.Title) };
                picker.FileTypeChoices.Add("Magic Capture documentation project", [".magicdoc"]);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file is null) return;
                path = file.Path;
            }
            await _services.DocumentationProjects.SaveAsync(path!, _project, _images, _logoPng);
            _projectPath = path;
            if (savedSessionId == _recoverySessionId && savedGeneration == _recoveryGeneration)
                _currentProjectDisplayName = Path.GetFileName(path);
            await HandleExplicitSaveSucceededAsync(savedSessionId, savedGeneration, savedRevision);
            ShowStatus("Documentation project saved locally.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationSave", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private async void ExportLongPng_Click(object sender, RoutedEventArgs e) =>
        await ExportFileAsync("PNG image", ".png", "-steps", (path, token) => _services.DocumentationExport.ExportLongPngAsync(path, _project, _images, _logoPng, token));

    private async void ExportPdf_Click(object sender, RoutedEventArgs e) =>
        await ExportFileAsync("PDF document", ".pdf", "-guide", (path, token) => _services.DocumentationExport.ExportPdfAsync(path, _project, _images, _logoPng, token));

    private async void ExportDocx_Click(object sender, RoutedEventArgs e) =>
        await ExportFileAsync("Word document", ".docx", "-guide", (path, token) => _services.DocumentationExport.ExportDocxAsync(path, _project, _images, _logoPng, token));

    private async void ExportOfflineHtml_Click(object sender, RoutedEventArgs e) =>
        await ExportFileAsync("Offline HTML", ".html", "-offline", (path, token) => _services.DocumentationExport.ExportOfflineHtmlAsync(path, _project, _images, _logoPng, token));

    private async void ExportHtml_Click(object sender, RoutedEventArgs e) =>
        await ExportFolderAsync("html", (path, token) => _services.DocumentationExport.ExportHtmlAsync(path, _project, _images, _logoPng, token));

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e) =>
        await ExportFolderAsync("markdown", (path, token) => _services.DocumentationExport.ExportMarkdownAsync(path, _project, _images, _logoPng, token));

    private async Task ExportFileAsync(string label, string extension, string suffix, Func<string, CancellationToken, Task> export)
    {
        if (_busy) return;
        _project = SyncProjectMetadata(_project);
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = SanitizeFileName(_project.Title) + suffix };
        picker.FileTypeChoices.Add(label, [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            SetBusy(true, $"Exporting {label}…");
            await export(file.Path, CancellationToken.None);
            ShowStatus($"Exported {label}: {file.Name}", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationExport", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task ExportFolderAsync(string suffix, Func<string, CancellationToken, Task> export)
    {
        if (_busy) return;
        _project = SyncProjectMetadata(_project);
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var parent = await picker.PickSingleFolderAsync();
        if (parent is null) return;
        var destination = Path.Combine(parent.Path, SanitizeFileName(_project.Title) + "-" + suffix);
        try
        {
            SetBusy(true, $"Exporting {suffix} folder…");
            await export(destination, CancellationToken.None);
            ShowStatus($"Exported folder: {destination}", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationFolderExport", ex);
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private async void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StepList.SelectedItem is not StepListItem selected)
        {
            StepPreviewImage.Source = null;
            StepTitleBox.Text = string.Empty;
            StepDescriptionBox.Text = string.Empty;
            StepSectionBox.Text = string.Empty;
            StepEvidenceText.Text = string.Empty;
            return;
        }
        StepTitleBox.Text = selected.Step.Title ?? string.Empty;
        StepDescriptionBox.Text = selected.Step.Description ?? string.Empty;
        StepSectionBox.Text = selected.Step.Section ?? string.Empty;
        StepEvidenceText.Text = BuildEvidenceText(selected.Step);
        if (_images.TryGetValue(selected.Step.ImageKey, out var bytes))
            await CaptureOverlayWindow.SetImageAsync(StepPreviewImage, bytes);
    }

    private void RefreshStepList(string? selectedId = null)
    {
        selectedId ??= (StepList.SelectedItem as StepListItem)?.Step.Id;
        StepList.Items.Clear();
        StepListItem? selection = null;
        for (var i = 0; i < _project.Steps.Count; i++)
        {
            var item = new StepListItem(i + 1, _project.Steps[i]);
            StepList.Items.Add(item);
            if (string.Equals(item.Step.Id, selectedId, StringComparison.Ordinal)) selection = item;
        }
        StepCountText.Text = $"{_project.Steps.Count} step{(_project.Steps.Count == 1 ? string.Empty : "s")}";
        StepList.SelectedItem = selection ?? (StepList.Items.Count > 0 ? StepList.Items[0] : null);
    }

    private DocumentationProject SyncProjectMetadata(DocumentationProject project) => DocumentationPolicy.Normalize(project with
    {
        Title = ProjectTitleBox.Text,
        Subtitle = NullIfWhiteSpace(ProjectSubtitleBox.Text),
        Header = NullIfWhiteSpace(ProjectHeaderBox.Text),
        Footer = NullIfWhiteSpace(ProjectFooterBox.Text),
        LogoImageKey = _logoPng is { Length: > 0 } ? "logo.png" : null,
        Template = SelectedTemplateId(),
        ModifiedUtc = DateTimeOffset.UtcNow
    });

    private void LoadProjectMetadata()
    {
        ProjectTitleBox.Text = _project.Title;
        ProjectSubtitleBox.Text = _project.Subtitle ?? string.Empty;
        ProjectHeaderBox.Text = _project.Header ?? string.Empty;
        ProjectFooterBox.Text = _project.Footer ?? string.Empty;
        SelectTemplate(_project.Template);
        UpdateLogoStatus();
    }

    private void InitializeTemplatePicker()
    {
        TemplateComboBox.Items.Clear();
        foreach (var profile in DocumentationTemplateCatalog.All)
            TemplateComboBox.Items.Add(new ComboBoxItem { Content = profile.DisplayName, Tag = profile.Id });
        SelectTemplate(DocumentationTemplateCatalog.DefaultId);
    }

    private string SelectedTemplateId()
    {
        if (TemplateComboBox.SelectedItem is ComboBoxItem item && item.Tag is string id)
            return DocumentationTemplateCatalog.NormalizeId(id);
        return DocumentationTemplateCatalog.DefaultId;
    }

    private void SelectTemplate(string? templateId)
    {
        var normalized = DocumentationTemplateCatalog.NormalizeId(templateId);
        foreach (var item in TemplateComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string id && string.Equals(id, normalized, StringComparison.Ordinal))
            {
                TemplateComboBox.SelectedItem = item;
                return;
            }
        }
        TemplateComboBox.SelectedIndex = TemplateComboBox.Items.Count > 0 ? 0 : -1;
    }

    private void UpdateLogoStatus(string? sourceName = null)
    {
        if (_logoPng is not { Length: > 0 })
        {
            LogoStatusText.Text = "No logo";
            return;
        }
        LogoStatusText.Text = string.IsNullOrWhiteSpace(sourceName) ? "Logo embedded in project" : $"Embedded: {sourceName}";
    }

    private void PruneUnreferencedImages()
    {
        var referenced = _project.Steps.Select(step => step.ImageKey).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _images.Keys.Where(key => !referenced.Contains(key)).ToArray()) _images.Remove(key);
    }

    private void ProjectMetadata_TextChanged(object sender, TextChangedEventArgs e) => ScheduleRecoveryAutosave();

    private void ProjectTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e) => ScheduleRecoveryAutosave();

    private void ScheduleRecoveryAutosave()
    {
        if (_suppressRecoveryAutosave || _closingCleanly) return;
        _dirtyRevision = checked(_dirtyRevision + 1);
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    private async void RecoveryTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        await FlushRecoveryAutosaveAsync();
    }

    private async Task FlushRecoveryAutosaveAsync()
    {
        if (_suppressRecoveryAutosave || _closingCleanly || _dirtyRevision <= _lastRecoveryRevision) return;
        var revision = _dirtyRevision;
        var sessionId = _recoverySessionId;
        var generation = _recoveryGeneration;
        var project = SyncProjectMetadata(_project);
        var images = _images.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var logoPng = _logoPng;
        var displayName = _currentProjectDisplayName ?? NullIfWhiteSpace(project.Title);

        await _recoveryWriteGate.WaitAsync();
        try
        {
            if (_closingCleanly || generation != _recoveryGeneration || sessionId != _recoverySessionId || revision <= _lastRecoveryRevision) return;
            await _services.DocumentationRecovery.SaveAsync(sessionId, project, images, logoPng, revision, displayName);
            if (generation != _recoveryGeneration || sessionId != _recoverySessionId) return;
            _lastRecoveryRevision = revision;
            if (!_closingCleanly && _dirtyRevision == revision)
                ShowStatus("Autosaved documentation recovery.", InfoBarSeverity.Informational);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationRecoveryAutosave", ex);
        }
        finally
        {
            _recoveryWriteGate.Release();
        }

        if (!_closingCleanly && _dirtyRevision > _lastRecoveryRevision)
        {
            _recoveryTimer.Stop();
            _recoveryTimer.Start();
        }
    }

    private async Task HandleExplicitSaveSucceededAsync(Guid savedSessionId, long savedGeneration, long savedRevision)
    {
        if (savedSessionId != _recoverySessionId || savedGeneration != _recoveryGeneration) return;
        _recoveryTimer.Stop();
        await _recoveryWriteGate.WaitAsync();
        try
        {
            if (savedSessionId != _recoverySessionId || savedGeneration != _recoveryGeneration) return;
            if (_dirtyRevision > savedRevision)
            {
                if (_dirtyRevision > _lastRecoveryRevision) _recoveryTimer.Start();
                return;
            }

            _recoveryGeneration = checked(_recoveryGeneration + 1);
            try
            {
                await _services.DocumentationRecovery.DeleteAsync(savedSessionId);
            }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
            {
                _services.Log.Error("DocumentationRecoverySaveCleanup", ex);
            }
            _lastRecoveryRevision = 0;
            if (_dirtyRevision <= savedRevision) _dirtyRevision = 0;
            else _recoveryTimer.Start();
        }
        finally
        {
            _recoveryWriteGate.Release();
        }
    }

    private async Task InvalidateAndDeleteRecoveryAsync()
    {
        _recoveryTimer.Stop();
        var sessionId = _recoverySessionId;
        _recoveryGeneration = checked(_recoveryGeneration + 1);
        await _recoveryWriteGate.WaitAsync();
        try
        {
            await _services.DocumentationRecovery.DeleteAsync(sessionId);
            if (sessionId == _recoverySessionId)
            {
                _dirtyRevision = 0;
                _lastRecoveryRevision = 0;
            }
        }
        finally
        {
            _recoveryWriteGate.Release();
        }
    }

    private async void DocumentationAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _recoveryTimer.Stop();
        if ((Application.Current as App)?.IsExitRequested == true) return;
        if (_closeCleanupComplete) return;

        args.Cancel = true;
        if (_closingCleanly) return;
        _closingCleanly = true;
        try
        {
            await InvalidateAndDeleteRecoveryAsync();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("DocumentationRecoveryClose", ex);
        }
        finally
        {
            _closeCleanupComplete = true;
            _closingCleanly = false;
        }
        if ((Application.Current as App)?.IsExitRequested != true) Close();
    }

    private async Task StopRecordingAsync(string status)
    {
        if (!_ownsRecording) return;
        _ownsRecording = false;
        try { await _services.StepRecorder.StopAsync(); }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { _services.Log.Error("DocumentationRecorderStop", ex); }
        StartStopButton.Content = "Start recording";
        ShowStatus(status, InfoBarSeverity.Informational);
    }

    private async void DocumentationWindow_Closed(object sender, WindowEventArgs args)
    {
        _services.StepRecorder.StepCaptured -= StepRecorder_StepCaptured;
        _services.StepRecorder.CaptureFailed -= StepRecorder_CaptureFailed;
        if (_ownsRecording)
        {
            _ownsRecording = false;
            try { await _services.StepRecorder.StopAsync(); }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { _services.Log.Error("DocumentationRecorderClose", ex); }
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        StartStopButton.IsEnabled = !busy;
        if (status is not null) ShowStatus(status, InfoBarSeverity.Informational);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string BuildEvidenceText(DocumentationStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.SafeKeyGesture)) return "Safe shortcut: " + step.SafeKeyGesture;
        if (step.Target is null) return step.ClickPoint is null ? "Imported image" : "Captured screen region";
        var control = step.Target.ControlType;
        var app = !string.IsNullOrWhiteSpace(step.Target.WindowTitle) ? step.Target.WindowTitle : step.Target.ProcessName;
        return string.IsNullOrWhiteSpace(app) ? control : $"{control} · {app}";
    }

    private static string SanitizeFileName(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "Magic Capture guide" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(source.Where(c => !invalid.Contains(c)).Take(96).ToArray()).Trim();
        return safe.Length == 0 ? "Magic Capture guide" : safe;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
