using DrawingRotateFlipType = System.Drawing.RotateFlipType;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Projects;
using Magic.Capture.Core.ScreenGraph;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Privacy;
using Windows.Storage.Pickers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Magic.Capture.App.Views;

public sealed partial class AnnotationWindow : Window
{
    private sealed record EditorState(byte[] BasePng, IReadOnlyList<AnnotationLayer> Layers);

    private readonly CaptureAsset _sourceAsset;
    private readonly ApplicationServices _services;
    private byte[] _basePng;
    private readonly List<AnnotationLayer> _layers = [];
    private readonly Stack<EditorState> _undo = [];
    private readonly Stack<EditorState> _redo = [];
    private bool _initialized;
    private bool _drawing;
    private Point _start;
    private Point _current;
    private readonly List<PixelPoint> _gesturePoints = [];
    private readonly List<AnnotationLayer> _layerClipboard = [];
    private int _imageWidth;
    private int _imageHeight;
    private Guid _projectId = Guid.NewGuid();
    private DateTimeOffset _projectCreatedUtc = DateTimeOffset.UtcNow;
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
    private AnnotationStylePreset? _activeStylePreset;
    private AnnotationKind PreferredAnnotationTool => RememberLastAnnotationTool && _services.Settings.LastAnnotationTool is { } last ? last : _services.Settings.DefaultAnnotationTool;
    private bool RememberLastAnnotationTool => _services.Settings.RememberLastAnnotationTool;

    internal AnnotationWindow(CaptureAsset asset, ApplicationServices services)
    {
        InitializeComponent();
        _sourceAsset = asset;
        _services = services;
        _basePng = asset.PngBytes;
        _imageWidth = asset.Width;
        _imageHeight = asset.Height;
        Platform.WindowHelpers.MoveAndResize(this, 120, 80, 1220, 820);
        var advanced = services.Entitlements.CanUse(ProductFeature.AdvancedEditor);
        HighlightTool.IsEnabled = advanced;
        BlurTool.IsEnabled = advanced;
        PixelateTool.IsEnabled = advanced;
        SelectComboByTag(ToolCombo, PreferredAnnotationTool.ToString());
        RefreshStylePresets();
        ApplyToolbarLayout();
        _appWindow = Platform.WindowHelpers.GetAppWindow(this);
        _recoveryTimer = DispatcherQueue.CreateTimer();
        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _recoveryTimer.IsRepeating = false;
        _recoveryTimer.Tick += RecoveryTimer_Tick;
        _appWindow.Closing += AnnotationAppWindow_Closing;
        Activated += OnActivated;
    }

    internal AnnotationWindow(
        EditableProjectPackage package,
        ApplicationServices services,
        Guid recoverySessionId,
        long dirtyRevision,
        string? originalProjectDisplayName)
        : this(CreateRecoveredAsset(package, originalProjectDisplayName), services)
    {
        ArgumentNullException.ThrowIfNull(package);
        _suppressRecoveryAutosave = true;
        try
        {
            _basePng = package.BasePng;
            _layers.Clear();
            _layers.AddRange(package.Manifest.Annotations.Layers);
            _imageWidth = package.Manifest.Width;
            _imageHeight = package.Manifest.Height;
            _projectId = package.Manifest.ProjectId;
            _projectCreatedUtc = package.Manifest.CreatedUtc;
            _recoverySessionId = recoverySessionId == Guid.Empty ? Guid.NewGuid() : recoverySessionId;
            _dirtyRevision = Math.Max(1, dirtyRevision);
            _lastRecoveryRevision = _dirtyRevision;
            _currentProjectDisplayName = originalProjectDisplayName;
        }
        finally
        {
            _suppressRecoveryAutosave = false;
        }
    }

    private static CaptureAsset CreateRecoveredAsset(EditableProjectPackage package, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(package);
        var manifest = package.Manifest;
        return new CaptureAsset(
            manifest.ProjectId,
            manifest.CreatedUtc,
            new PixelRect(0, 0, manifest.Width, manifest.Height),
            package.BasePng,
            manifest.Width,
            manifest.Height,
            CaptureSourceKind.Region,
            string.IsNullOrWhiteSpace(displayName) ? "Recovered editable project" : displayName);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized || args.WindowActivationState == WindowActivationState.Deactivated) return;
        _initialized = true;
        await RefreshPreviewAsync();
    }

    private AnnotationKind? CurrentTool()
    {
        var tag = (ToolCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        return Enum.TryParse<AnnotationKind>(tag, out var kind) ? kind : null;
    }

    private void ToolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (ToolCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var needsText = tag is "Text" or "SpeechBalloon" or "Callout" or "Emoji";
        TextInput.Visibility = needsText ? Visibility.Visible : Visibility.Collapsed;
        TextInput.PlaceholderText = tag == "Emoji" ? "Emoji, e.g. ✅" : "Text annotation";
        StatusText.Text = tag == "Color"
            ? "Click a pixel to copy its HEX value."
            : $"{tag ?? "Rectangle"} tool selected.";
        if (_initialized && Enum.TryParse<AnnotationKind>(tag, out var selectedKind))
            _ = PersistLastAnnotationToolAsync(selectedKind);
    }

    private async Task PersistLastAnnotationToolAsync(AnnotationKind kind)
    {
        if (!RememberLastAnnotationTool || _services.Settings.LastAnnotationTool == kind) return;
        _ = await ((App)Application.Current).TryMutateSettingsAsync(
            current => current with { LastAnnotationTool = kind },
            logComponent: "AnnotationLastToolSave");
    }

    private void RefreshStylePresets()
    {
        StylePresetCombo.ItemsSource = _services.Settings.AnnotationStylePresets.ToArray();
        if (_activeStylePreset is not null)
            StylePresetCombo.SelectedItem = _services.Settings.AnnotationStylePresets.FirstOrDefault(item => item.Id == _activeStylePreset.Id);
    }

    private void ApplyToolbarLayout()
    {
        var map = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["undo"] = ToolbarUndoButton, ["redo"] = ToolbarRedoButton, ["rotate"] = ToolbarRotateButton,
            ["resize"] = ToolbarResizeButton, ["copy"] = ToolbarCopyButton, ["save"] = ToolbarSaveButton, ["pin"] = ToolbarPinButton
        };
        foreach (var button in map.Values) EditorToolbar.Children.Remove(button);
        foreach (var item in _services.Settings.ToolbarActions)
        {
            if (!map.TryGetValue(item.Id, out var button)) continue;
            button.Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed;
            EditorToolbar.Children.Add(button);
        }
    }

    private void ApplyAnnotationStylePreset(AnnotationStylePreset preset)
    {
        _activeStylePreset = preset;
        StrokeSlider.Value = preset.StrokeWidth;
        StrokeColorBox.Text = FormatArgb(preset.Argb);
        LayerOpacityBox.Value = Math.Round(preset.Opacity * 100d);
        FillColorBox.Text = preset.FillArgb is { } fill ? FormatArgb(fill) : string.Empty;
        FontFamilyBox.Text = preset.FontFamily;
        LayerFontSizeBox.Value = preset.FontSize;
        LayerBoldCheck.IsChecked = preset.FontBold;
        LayerItalicCheck.IsChecked = preset.FontItalic;
        SelectComboByTag(TextAlignmentCombo, preset.TextAlignment.ToString());
        StatusText.Text = $"Style preset applied: {preset.Name}.";
    }

    private async Task SaveAnnotationStylePresetAsync()
    {
        var name = StylePresetName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) { StatusText.Text = "Enter a style name first."; return; }
        uint? stroke = null;
        if (!TryParseArgb(StrokeColorBox.Text, out stroke) || stroke is null) stroke = _activeStylePreset?.Argb ?? 0xFFFF3B30;
        uint? fill = null;
        if (!string.IsNullOrWhiteSpace(FillColorBox.Text)) TryParseArgb(FillColorBox.Text, out fill);
        var existing = StylePresetCombo.SelectedItem as AnnotationStylePreset;
        var alignTag = (TextAlignmentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var align = Enum.TryParse<AnnotationTextAlignment>(alignTag, out var parsed) ? parsed : AnnotationTextAlignment.Left;
        var preset = new AnnotationStylePreset(
            existing?.Id ?? Guid.NewGuid().ToString("N"), name, stroke.Value, (float)StrokeSlider.Value,
            double.IsFinite(LayerOpacityBox.Value) ? (float)Math.Clamp(LayerOpacityBox.Value / 100d, 0.05d, 1d) : 1f,
            fill, FontFamilyBox.Text, double.IsFinite(LayerFontSizeBox.Value) ? (float)LayerFontSizeBox.Value : 18f,
            LayerBoldCheck.IsChecked == true, LayerItalicCheck.IsChecked == true, align);
        var saved = await ((App)Application.Current).TryMutateSettingsAsync(
            current => current with
            {
                AnnotationStylePresets = current.AnnotationStylePresets.Where(item => item.Id != preset.Id).Append(preset).ToArray()
            },
            logComponent: "AnnotationStyleSave");
        if (!saved) { StatusText.Text = "Style was not saved because settings storage is unavailable."; return; }
        _activeStylePreset = _services.Settings.AnnotationStylePresets.FirstOrDefault(item => item.Id == preset.Id);
        RefreshStylePresets();
        StylePresetCombo.SelectedItem = _activeStylePreset;
        StatusText.Text = "Style preset saved.";
    }

    private async void ApplyStylePreset_Click(object sender, RoutedEventArgs e)
    {
        if (StylePresetCombo.SelectedItem is not AnnotationStylePreset preset) return;
        ApplyAnnotationStylePreset(preset);
        if (SelectedLayerIds.Count > 0)
        {
            var update = new AnnotationStyleUpdate(preset.Argb, preset.StrokeWidth, preset.Opacity, FillArgb: preset.FillArgb,
                ClearFill: preset.FillArgb is null, FontFamily: preset.FontFamily, FontSize: preset.FontSize,
                FontBold: preset.FontBold, FontItalic: preset.FontItalic, TextAlignment: preset.TextAlignment);
            await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.SetStyle(document, SelectedLayerIds, update), "Style preset applied.", SelectedLayerIds);
        }
    }

    private async void SaveStylePreset_Click(object sender, RoutedEventArgs e) => await SaveAnnotationStylePresetAsync();

    private async void DeleteStylePreset_Click(object sender, RoutedEventArgs e)
    {
        if (StylePresetCombo.SelectedItem is not AnnotationStylePreset preset) return;
        var deleted = await ((App)Application.Current).TryMutateSettingsAsync(
            current => current with
            {
                AnnotationStylePresets = current.AnnotationStylePresets.Where(item => item.Id != preset.Id).ToArray()
            },
            logComponent: "AnnotationStyleDelete");
        if (!deleted) { StatusText.Text = "Style was not deleted because settings storage is unavailable."; return; }
        if (_activeStylePreset?.Id == preset.Id) _activeStylePreset = null;
        RefreshStylePresets();
        StatusText.Text = "Style preset deleted.";
    }

    private void InteractionCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(InteractionCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;
        _start = point.Position;
        _current = _start;

        if (((ToolCombo.SelectedItem as ComboBoxItem)?.Tag as string) == "Color")
        {
            SampleColor(_start);
            return;
        }

        _drawing = true;
        _gesturePoints.Clear();
        _gesturePoints.Add(ToPixel(_start));
        InteractionCanvas.CapturePointer(e.Pointer);
        GestureRect.Visibility = Visibility.Visible;
        UpdateGestureVisual();
    }

    private void InteractionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_drawing) return;
        _current = e.GetCurrentPoint(InteractionCanvas).Position;
        var kind = CurrentTool();
        if (kind is AnnotationKind.Freehand or AnnotationKind.Highlight)
            _gesturePoints.Add(ToPixel(_current));
        UpdateGestureVisual();
    }

    private async void InteractionCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_drawing) return;
        _current = e.GetCurrentPoint(InteractionCanvas).Position;
        _drawing = false;
        InteractionCanvas.ReleasePointerCapture(e.Pointer);
        GestureRect.Visibility = Visibility.Collapsed;

        var kind = CurrentTool();
        if (kind is null) return;
        var bounds = SelectionMath.FromPoints(ToPixel(_start), ToPixel(_current));
        if (kind is AnnotationKind.Freehand or AnnotationKind.Highlight)
        {
            _gesturePoints.Add(ToPixel(_current));
            if (_gesturePoints.Count < 2) return;
            bounds = BoundsForPoints(_gesturePoints);
        }
        else if (kind is AnnotationKind.Text or AnnotationKind.SpeechBalloon or AnnotationKind.Callout)
        {
            var text = TextInput.Text?.Trim();
            if (string.IsNullOrEmpty(text)) { StatusText.Text = "Enter annotation text first."; return; }
            if (bounds.IsEmpty) bounds = new PixelRect(ToPixel(_start).X, ToPixel(_start).Y, 160, 72);
        }
        else if (kind == AnnotationKind.Emoji)
        {
            if (bounds.IsEmpty) bounds = new PixelRect(ToPixel(_start).X, ToPixel(_start).Y, 64, 64);
        }
        else if (kind is AnnotationKind.StepNumber or AnnotationKind.StepAlpha or AnnotationKind.StepRoman or AnnotationKind.CursorStamp or AnnotationKind.ClickStamp)
        {
            if (bounds.IsEmpty) bounds = new PixelRect(ToPixel(_start).X, ToPixel(_start).Y, 48, 48);
        }
        else if (bounds.IsEmpty)
        {
            return;
        }

        string? layerText = null;
        if (kind is AnnotationKind.Text or AnnotationKind.SpeechBalloon or AnnotationKind.Callout) layerText = TextInput.Text;
        else if (kind == AnnotationKind.Emoji) layerText = string.IsNullOrWhiteSpace(TextInput.Text) ? "🙂" : TextInput.Text.Trim();
        else if (kind is AnnotationKind.StepNumber or AnnotationKind.StepAlpha or AnnotationKind.StepRoman)
        {
            var stepIndex = _layers.Count(layer => layer.Kind == kind) + 1;
            layerText = kind switch
            {
                AnnotationKind.StepAlpha => AnnotationStepLabels.Alpha(stepIndex),
                AnnotationKind.StepRoman => AnnotationStepLabels.Roman(stepIndex),
                _ => AnnotationStepLabels.Number(stepIndex),
            };
        }

        PushUndo();
        var newLayer = new AnnotationLayer(
            kind.Value,
            bounds,
            kind is AnnotationKind.Freehand or AnnotationKind.Highlight ? _gesturePoints.ToArray() : null,
            StrokeWidth: (float)StrokeSlider.Value,
            Text: layerText);
        if (_activeStylePreset is { } preset)
        {
            newLayer = newLayer with
            {
                Argb = preset.Argb,
                StrokeWidth = preset.StrokeWidth,
                Opacity = preset.Opacity,
                FillArgb = preset.FillArgb,
                FontFamily = preset.FontFamily,
                FontSize = preset.FontSize,
                FontBold = preset.FontBold,
                FontItalic = preset.FontItalic,
                TextAlignment = preset.TextAlignment
            };
        }
        _layers.Add(newLayer);
        _redo.Clear();

        if (kind == AnnotationKind.Crop)
        {
            CommitRenderedAsBase();
            StatusText.Text = $"Cropped to {_imageWidth} × {_imageHeight}.";
        }
        else
        {
            StatusText.Text = $"Added {kind}.";
        }
        ScheduleRecoveryAutosave();
        await RefreshPreviewAsync();
    }

    private void UpdateGestureVisual()
    {
        var left = Math.Min(_start.X, _current.X);
        var top = Math.Min(_start.Y, _current.Y);
        GestureRect.Width = Math.Abs(_current.X - _start.X);
        GestureRect.Height = Math.Abs(_current.Y - _start.Y);
        Canvas.SetLeft(GestureRect, left);
        Canvas.SetTop(GestureRect, top);
    }

    private PixelPoint ToPixel(Point point)
    {
        var x = (int)Math.Round(point.X / Math.Max(1, InteractionCanvas.ActualWidth) * _imageWidth);
        var y = (int)Math.Round(point.Y / Math.Max(1, InteractionCanvas.ActualHeight) * _imageHeight);
        return new PixelPoint(Math.Clamp(x, 0, Math.Max(0, _imageWidth - 1)), Math.Clamp(y, 0, Math.Max(0, _imageHeight - 1)));
    }

    private static PixelRect BoundsForPoints(IReadOnlyList<PixelPoint> points)
    {
        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        var right = points.Max(p => p.X);
        var bottom = points.Max(p => p.Y);
        return new PixelRect(left, top, Math.Max(1, right - left + 1), Math.Max(1, bottom - top + 1));
    }

    private byte[] RenderCurrent() => _services.AnnotationRenderer.Render(_basePng, new AnnotationDocument(_layers.ToArray()));

    private void CommitRenderedAsBase()
    {
        _basePng = RenderCurrent();
        using var bitmap = Imaging.BitmapCodec.Decode(_basePng);
        _imageWidth = bitmap.Width;
        _imageHeight = bitmap.Height;
        _layers.Clear();
    }

    private EditorState Snapshot() => new(_basePng, _layers.ToArray());

    private void Restore(EditorState state)
    {
        _basePng = state.BasePng;
        _layers.Clear();
        _layers.AddRange(state.Layers);
        using var bitmap = Imaging.BitmapCodec.Decode(RenderCurrent());
        _imageWidth = bitmap.Width;
        _imageHeight = bitmap.Height;
    }

    private void PushUndo()
    {
        _undo.Push(Snapshot());
        while (_undo.Count > 100)
        {
            var keep = _undo.Reverse().Skip(1).Reverse().ToArray();
            _undo.Clear();
            foreach (var state in keep) _undo.Push(state);
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        ScheduleRecoveryAutosave();
        await RefreshPreviewAsync();
        StatusText.Text = "Undo.";
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        ScheduleRecoveryAutosave();
        await RefreshPreviewAsync();
        StatusText.Text = "Redo.";
    }

    private async void Rotate_Click(object sender, RoutedEventArgs e) => await ApplyDestructiveTransformAsync(bytes => _services.Transforms.Rotate(bytes, DrawingRotateFlipType.Rotate90FlipNone), "Rotated 90°." );
    private async void FlipH_Click(object sender, RoutedEventArgs e) => await ApplyDestructiveTransformAsync(bytes => _services.Transforms.Rotate(bytes, DrawingRotateFlipType.RotateNoneFlipX), "Flipped horizontally." );
    private async void FlipV_Click(object sender, RoutedEventArgs e) => await ApplyDestructiveTransformAsync(bytes => _services.Transforms.Rotate(bytes, DrawingRotateFlipType.RotateNoneFlipY), "Flipped vertically." );

    private async Task ApplyDestructiveTransformAsync(Func<byte[], byte[]> transform, string message)
    {
        PushUndo();
        _redo.Clear();
        _basePng = transform(RenderCurrent());
        _layers.Clear();
        using var bitmap = Imaging.BitmapCodec.Decode(_basePng);
        _imageWidth = bitmap.Width;
        _imageHeight = bitmap.Height;
        ScheduleRecoveryAutosave();
        await RefreshPreviewAsync();
        StatusText.Text = message;
    }

    private async void Resize_Click(object sender, RoutedEventArgs e)
    {
        var width = new NumberBox { Header = "Width", Minimum = 1, Maximum = 32768, Value = _imageWidth, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var height = new NumberBox { Header = "Height", Minimum = 1, Maximum = 32768, Value = _imageHeight, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(width);
        panel.Children.Add(height);
        var dialog = new ContentDialog { Title = "Resize image", Content = panel, PrimaryButtonText = "Resize", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ApplyDestructiveTransformAsync(bytes => _services.Transforms.Resize(bytes, (int)width.Value, (int)height.Value), $"Resized to {(int)width.Value} × {(int)height.Value}." );
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        await _services.Clipboard.CopyImageAsync(RenderCurrent());
        StatusText.Text = "Flattened image copied.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var bytes = RenderCurrent();
        using var bitmap = Imaging.BitmapCodec.Decode(bytes);
        var asset = new CaptureAsset(Guid.NewGuid(), DateTimeOffset.UtcNow, new PixelRect(0, 0, bitmap.Width, bitmap.Height), bytes, bitmap.Width, bitmap.Height, CaptureSourceKind.Region, "Edited capture");
        var file = await _services.Export.SaveImageAsAsync(this, asset, "png", _services.Settings.JpegQuality, _services.Settings.FileNameTemplate);
        StatusText.Text = file is null ? "Save cancelled." : $"Saved {file.Name}";
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var bytes = RenderCurrent();
        using var bitmap = Imaging.BitmapCodec.Decode(bytes);
        var asset = new CaptureAsset(Guid.NewGuid(), DateTimeOffset.UtcNow, new PixelRect(0, 0, bitmap.Width, bitmap.Height), bytes, bitmap.Width, bitmap.Height, CaptureSourceKind.Region, "Edited capture");
        ((App)Application.Current).OpenPin(asset);
    }


    private async void SmartRedact_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Scanning locally for sensitive data…";
            var bytes = RenderCurrent();
            var asset = _sourceAsset.WithPng(bytes) with { Id = Guid.NewGuid(), PixelBounds = new PixelRect(0, 0, _imageWidth, _imageHeight) };
            var analysis = await _services.Analysis.AnalyzeAsync(asset, _services.Settings);
            var graph = ScreenGraphBuilder.Build(new ScreenGraphBuildInput(
                asset.Id, asset.CreatedUtc, asset.SourceKind.ToString(), asset.SourceDisplayName, asset.Width, asset.Height,
                new PixelRect(0, 0, asset.Width, asset.Height), analysis.Ocr, analysis.Table,
                analysis.Barcodes.Select(hit => new ScreenBarcode(hit.Format, hit.Text, hit.Bounds ?? new PixelRect(0, 0, asset.Width, asset.Height))).ToArray()));
            var findings = SensitiveDataDetector.Scan(graph);
            var plan = RedactionPlanner.Create(findings, new PixelRect(0, 0, asset.Width, asset.Height), RedactionStyle.Pixelate, padding: 4);
            if (plan.Layers.Count == 0)
            {
                StatusText.Text = "No common sensitive data found. Nothing was changed.";
                return;
            }

            PushUndo();
            _redo.Clear();
            _layers.AddRange(plan.Layers);
            ScheduleRecoveryAutosave();
            await RefreshPreviewAsync();
            StatusText.Text = $"Added {plan.Layers.Count} local redaction layer(s). Review them, then Undo if needed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Smart Redact: {ex.Message}";
            _services.Log.Error("SmartRedact", ex);
        }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "Magic Capture Desktop project" };
        picker.FileTypeChoices.Add("Magic Capture Desktop project", new List<string> { ".magiccapture" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            var savedRevision = _dirtyRevision;
            var savedSessionId = _recoverySessionId;
            var savedGeneration = _recoveryGeneration;
            var manifest = BuildProjectManifest();
            var basePng = _basePng;
            await _services.EditableProjects.SaveAsync(file.Path, basePng, manifest);
            if (savedSessionId == _recoverySessionId && savedGeneration == _recoveryGeneration)
                _currentProjectDisplayName = file.Name;
            await HandleExplicitSaveSucceededAsync(savedSessionId, savedGeneration, savedRevision);
            StatusText.Text = $"Saved editable project: {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".magiccapture");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var package = await _services.EditableProjects.LoadAsync(file.Path);
            _suppressRecoveryAutosave = true;
            try
            {
                await InvalidateAndDeleteRecoveryAsync();
                _undo.Clear();
                _redo.Clear();
                _basePng = package.BasePng;
                _layers.Clear();
                _layers.AddRange(package.Manifest.Annotations.Layers);
                _imageWidth = package.Manifest.Width;
                _imageHeight = package.Manifest.Height;
                _projectId = package.Manifest.ProjectId;
                _projectCreatedUtc = package.Manifest.CreatedUtc;
                _currentProjectDisplayName = file.Name;
                _recoverySessionId = Guid.NewGuid();
                _dirtyRevision = 0;
                _lastRecoveryRevision = 0;
            }
            finally
            {
                _suppressRecoveryAutosave = false;
            }
            await RefreshPreviewAsync();
            StatusText.Text = $"Opened editable project: {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private EditableProjectManifest BuildProjectManifest() =>
        new(
            EditableProjectManifest.CurrentSchemaVersion,
            EditableProjectManifest.ProductName,
            _projectId,
            _projectCreatedUtc,
            DateTimeOffset.UtcNow,
            _imageWidth,
            _imageHeight,
            new AnnotationDocument(_layers.ToArray()),
            Metadata: new Dictionary<string, string>
            {
                ["sourceKind"] = _sourceAsset.SourceKind.ToString(),
                ["sourceDisplayName"] = _sourceAsset.SourceDisplayName ?? string.Empty,
                ["captureId"] = _sourceAsset.Id.ToString("N")
            });

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
        var basePng = _basePng;
        var manifest = BuildProjectManifest();
        await _recoveryWriteGate.WaitAsync();
        try
        {
            if (_closingCleanly
                || generation != _recoveryGeneration
                || sessionId != _recoverySessionId
                || revision <= _lastRecoveryRevision) return;

            await _services.EditableProjectRecovery.SaveAsync(
                sessionId,
                basePng,
                manifest,
                revision,
                _currentProjectDisplayName);

            if (generation != _recoveryGeneration || sessionId != _recoverySessionId) return;
            _lastRecoveryRevision = revision;
            if (!_closingCleanly && _dirtyRevision == revision) StatusText.Text = "Autosaved recovery.";
        }
        catch (Exception ex)
        {
            _services.Log.Error("EditableProjectAutosave", ex);
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

            var clearGeneration = checked(_recoveryGeneration + 1);
            _recoveryGeneration = clearGeneration;
            await _services.EditableProjectRecovery.DeleteAsync(savedSessionId);
            _lastRecoveryRevision = 0;

            if (_dirtyRevision <= savedRevision)
            {
                _dirtyRevision = 0;
            }
            else
            {
                _recoveryTimer.Stop();
                _recoveryTimer.Start();
            }
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
            await _services.EditableProjectRecovery.DeleteAsync(sessionId);
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

    private async void AnnotationAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
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
        catch (Exception ex)
        {
            _services.Log.Error("EditableProjectRecoveryClose", ex);
        }
        finally
        {
            _closeCleanupComplete = true;
            _closingCleanly = false;
        }
        if ((Application.Current as App)?.IsExitRequested != true) Close();
    }

    private AnnotationLayer? SelectedLayer => LayerList.SelectedItems.OfType<AnnotationLayer>().FirstOrDefault()
        ?? LayerList.SelectedItem as AnnotationLayer;

    private IReadOnlyList<AnnotationLayer> SelectedLayers => LayerList.SelectedItems.OfType<AnnotationLayer>().ToArray();

    private IReadOnlyList<string> SelectedLayerIds => SelectedLayers.Select(layer => layer.Id).ToArray();

    private async Task ApplyLayerEditAsync(Func<AnnotationDocument, AnnotationDocument> edit, string message, string? selectLayerId = null)
    {
        await ApplyLayerEditManyAsync(edit, message, string.IsNullOrWhiteSpace(selectLayerId) ? null : [selectLayerId]);
    }

    private async Task ApplyLayerEditManyAsync(Func<AnnotationDocument, AnnotationDocument> edit, string message, IEnumerable<string>? selectLayerIds = null)
    {
        try
        {
            var selectedIds = selectLayerIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()
                ?? SelectedLayerIds.ToArray();
            PushUndo();
            var edited = edit(new AnnotationDocument(_layers.ToArray()));
            _layers.Clear();
            _layers.AddRange(edited.Layers);
            _redo.Clear();
            ScheduleRecoveryAutosave();
            await RefreshPreviewManyAsync(selectedIds);
            StatusText.Text = message;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void LayerCopy_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLayers;
        if (selected.Count == 0) return;
        _layerClipboard.Clear();
        _layerClipboard.AddRange(selected);
        StatusText.Text = $"Copied {selected.Count} annotation layer(s).";
    }

    private async void LayerPaste_Click(object sender, RoutedEventArgs e)
    {
        if (_layerClipboard.Count == 0) return;
        var before = new HashSet<string>(_layers.Select(item => item.Id), StringComparer.Ordinal);
        await ApplyLayerEditManyAsync(
            document => AnnotationDocumentEditor.AppendCopies(document, _layerClipboard, 8, 8),
            $"Pasted {_layerClipboard.Count} annotation layer(s).",
            []);
        var pasted = _layers.Where(item => !before.Contains(item.Id)).Select(item => item.Id).ToArray();
        await RefreshPreviewManyAsync(pasted);
    }

    private async void LayerDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedLayerIds;
        if (ids.Count == 0) return;
        var before = new HashSet<string>(_layers.Select(item => item.Id), StringComparer.Ordinal);
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.DuplicateMany(document, ids), $"Duplicated {ids.Count} layer(s).", []);
        var copies = _layers.Where(item => !before.Contains(item.Id)).Select(item => item.Id).ToArray();
        await RefreshPreviewManyAsync(copies);
    }

    private async void LayerDelete_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedLayerIds;
        if (ids.Count == 0) return;
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.RemoveMany(document, ids), $"Deleted {ids.Count} layer(s).", []);
    }

    private async void LayerGroup_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedLayerIds;
        if (ids.Count < 2) { StatusText.Text = "Select at least two layers to group."; return; }
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.Group(document, ids), "Grouped selected layers.", ids);
    }

    private async void LayerUngroup_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedLayerIds;
        if (ids.Count == 0) return;
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.Ungroup(document, ids), "Ungrouped selected layers.", ids);
    }

    private async void LayerBack_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        await ApplyLayerEditAsync(document => AnnotationDocumentEditor.SendToBack(document, layer.Id), "Sent layer to back.", layer.Id);
    }

    private async void LayerBackward_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        await ApplyLayerEditAsync(document => AnnotationDocumentEditor.SendBackward(document, layer.Id), "Moved layer backward.", layer.Id);
    }

    private async void LayerForward_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        await ApplyLayerEditAsync(document => AnnotationDocumentEditor.BringForward(document, layer.Id), "Moved layer forward.", layer.Id);
    }

    private async void LayerFront_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        await ApplyLayerEditAsync(document => AnnotationDocumentEditor.BringToFront(document, layer.Id), "Brought layer to front.", layer.Id);
    }

    private async void LayerVisibility_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLayers;
        if (selected.Count == 0) return;
        var show = selected.Any(layer => !layer.IsVisible);
        await ApplyLayerEditManyAsync(document =>
        {
            foreach (var layer in selected) document = AnnotationDocumentEditor.SetVisibility(document, layer.Id, show);
            return document;
        }, show ? "Selected layers shown." : "Selected layers hidden.", selected.Select(layer => layer.Id));
    }

    private async void LayerLock_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLayers;
        if (selected.Count == 0) return;
        var lockLayers = selected.Any(layer => !layer.IsLocked);
        await ApplyLayerEditManyAsync(document =>
        {
            foreach (var layer in selected) document = AnnotationDocumentEditor.SetLocked(document, layer.Id, lockLayers);
            return document;
        }, lockLayers ? "Selected layers locked." : "Selected layers unlocked.", selected.Select(layer => layer.Id));
    }

    private async Task NudgeSelectedLayerAsync(int dx, int dy)
    {
        var ids = SelectedLayerIds;
        if (ids.Count == 0) return;
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.MoveMany(document, ids, dx, dy), "Selected layer(s) moved.", ids);
    }

    private async void LayerLeft_Click(object sender, RoutedEventArgs e) => await NudgeSelectedLayerAsync(-1, 0);
    private async void LayerRight_Click(object sender, RoutedEventArgs e) => await NudgeSelectedLayerAsync(1, 0);
    private async void LayerUp_Click(object sender, RoutedEventArgs e) => await NudgeSelectedLayerAsync(0, -1);
    private async void LayerDown_Click(object sender, RoutedEventArgs e) => await NudgeSelectedLayerAsync(0, 1);

    private async void LayerRotate_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLayers;
        if (selected.Count == 0) return;
        await ApplyLayerEditManyAsync(document =>
        {
            foreach (var layer in selected) document = AnnotationDocumentEditor.SetRotation(document, layer.Id, layer.RotationDegrees + 15);
            return document;
        }, "Selected layer(s) rotated 15°.", selected.Select(layer => layer.Id));
    }

    private async Task AlignSelectedAsync(AnnotationAlignment alignment, string message)
    {
        var ids = SelectedLayerIds;
        if (ids.Count < 2) { StatusText.Text = "Select at least two layers."; return; }
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.Align(document, ids, alignment), message, ids);
    }

    private async void AlignLeft_Click(object sender, RoutedEventArgs e) => await AlignSelectedAsync(AnnotationAlignment.Left, "Aligned left.");
    private async void AlignRight_Click(object sender, RoutedEventArgs e) => await AlignSelectedAsync(AnnotationAlignment.Right, "Aligned right.");
    private async void AlignTop_Click(object sender, RoutedEventArgs e) => await AlignSelectedAsync(AnnotationAlignment.Top, "Aligned top.");
    private async void AlignBottom_Click(object sender, RoutedEventArgs e) => await AlignSelectedAsync(AnnotationAlignment.Bottom, "Aligned bottom.");

    private async Task MatchSelectedSizeAsync(AnnotationMatchSize size, string message)
    {
        var ids = SelectedLayerIds;
        if (ids.Count < 2) { StatusText.Text = "Select at least two layers."; return; }
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.MatchSize(document, ids, size), message, ids);
    }

    private async void MatchWidth_Click(object sender, RoutedEventArgs e) => await MatchSelectedSizeAsync(AnnotationMatchSize.Width, "Matched widths.");
    private async void MatchHeight_Click(object sender, RoutedEventArgs e) => await MatchSelectedSizeAsync(AnnotationMatchSize.Height, "Matched heights.");
    private async void MatchBoth_Click(object sender, RoutedEventArgs e) => await MatchSelectedSizeAsync(AnnotationMatchSize.Both, "Matched sizes.");

    private async Task DistributeSelectedAsync(AnnotationDistribution distribution, string message)
    {
        var ids = SelectedLayerIds;
        if (ids.Count < 3) { StatusText.Text = "Select at least three layers to distribute."; return; }
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.Distribute(document, ids, distribution), message, ids);
    }

    private async void DistributeHorizontal_Click(object sender, RoutedEventArgs e) => await DistributeSelectedAsync(AnnotationDistribution.Horizontal, "Distributed horizontally.");
    private async void DistributeVertical_Click(object sender, RoutedEventArgs e) => await DistributeSelectedAsync(AnnotationDistribution.Vertical, "Distributed vertically.");

    private async void ApplyLayerBounds_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        if (!double.IsFinite(LayerXBox.Value) || !double.IsFinite(LayerYBox.Value) || !double.IsFinite(LayerWBox.Value) || !double.IsFinite(LayerHBox.Value))
        {
            StatusText.Text = "Bounds must be finite numbers.";
            return;
        }
        var bounds = new PixelRect((int)Math.Round(LayerXBox.Value), (int)Math.Round(LayerYBox.Value),
            Math.Max(1, (int)Math.Round(LayerWBox.Value)), Math.Max(1, (int)Math.Round(LayerHBox.Value)));
        await ApplyLayerEditAsync(document => AnnotationDocumentEditor.Resize(document, layer.Id, bounds), "Layer bounds updated.", layer.Id);
    }

    private async void ApplyLayerStyle_Click(object sender, RoutedEventArgs e)
    {
        var ids = SelectedLayerIds;
        if (ids.Count == 0) return;
        uint? stroke = null;
        if (!string.IsNullOrWhiteSpace(StrokeColorBox.Text) && !TryParseArgb(StrokeColorBox.Text, out stroke))
        {
            StatusText.Text = "Stroke color must be #RGB, #RRGGBB, or #AARRGGBB.";
            return;
        }
        uint? fill = null;
        var clearFill = string.IsNullOrWhiteSpace(FillColorBox.Text);
        if (!clearFill && !TryParseArgb(FillColorBox.Text, out fill))
        {
            StatusText.Text = "Fill color must be empty, #RGB, #RRGGBB, or #AARRGGBB.";
            return;
        }
        var lineStyleTag = (LineStyleCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var textAlignTag = (TextAlignmentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var lineStyle = Enum.TryParse<AnnotationLineStyle>(lineStyleTag, out var parsedLine) ? parsedLine : AnnotationLineStyle.Solid;
        var textAlignment = Enum.TryParse<AnnotationTextAlignment>(textAlignTag, out var parsedTextAlign) ? parsedTextAlign : AnnotationTextAlignment.Left;
        var opacity = double.IsFinite(LayerOpacityBox.Value) ? (float)Math.Clamp(LayerOpacityBox.Value / 100d, 0d, 1d) : 1f;
        var fontSize = double.IsFinite(LayerFontSizeBox.Value) ? (float)Math.Clamp(LayerFontSizeBox.Value, 8d, 256d) : 18f;
        var update = new AnnotationStyleUpdate(
            Argb: stroke,
            Opacity: opacity,
            LineStyle: lineStyle,
            FillArgb: fill,
            ClearFill: clearFill,
            FontFamily: FontFamilyBox.Text,
            FontSize: fontSize,
            FontBold: LayerBoldCheck.IsChecked == true,
            FontItalic: LayerItalicCheck.IsChecked == true,
            TextAlignment: textAlignment);
        await ApplyLayerEditManyAsync(document => AnnotationDocumentEditor.SetStyle(document, ids, update), "Style updated.", ids);
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        LayerXBox.Value = layer.Bounds.X;
        LayerYBox.Value = layer.Bounds.Y;
        LayerWBox.Value = layer.Bounds.Width;
        LayerHBox.Value = layer.Bounds.Height;
        StrokeColorBox.Text = FormatArgb(layer.Argb);
        FillColorBox.Text = layer.FillArgb is { } fill ? FormatArgb(fill) : string.Empty;
        LayerOpacityBox.Value = Math.Round(layer.Opacity * 100d);
        FontFamilyBox.Text = layer.FontFamily;
        LayerFontSizeBox.Value = layer.FontSize;
        LayerBoldCheck.IsChecked = layer.FontBold;
        LayerItalicCheck.IsChecked = layer.FontItalic;
        SelectComboByTag(LineStyleCombo, layer.LineStyle.ToString());
        SelectComboByTag(TextAlignmentCombo, layer.TextAlignment.ToString());
        StatusText.Text = SelectedLayers.Count > 1 ? $"{SelectedLayers.Count} layers selected." : $"Selected {layer.Kind}.";
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatArgb(uint value) => $"#{value:X8}";

    private static bool TryParseArgb(string? text, out uint? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var raw = text.Trim().TrimStart('#');
        if (raw.Length == 3)
            raw = $"FF{raw[0]}{raw[0]}{raw[1]}{raw[1]}{raw[2]}{raw[2]}";
        else if (raw.Length == 6)
            raw = "FF" + raw;
        if (raw.Length != 8 || !uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return false;
        value = parsed;
        return true;
    }

    private void SampleColor(Point point)
    {
        try
        {
            var pixel = ToPixel(point);
            var color = _services.Transforms.Sample(RenderCurrent(), pixel.X, pixel.Y);
            ColorText.Text = $"{color.Hex}   {color.Rgb}   {color.Hsl}   A={color.A}";
            _services.Clipboard.CopyText(color.Hex);
            StatusText.Text = $"Copied {color.Hex}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private Task RefreshPreviewAsync(string? selectedLayerId = null) =>
        RefreshPreviewManyAsync(string.IsNullOrWhiteSpace(selectedLayerId) ? SelectedLayerIds : [selectedLayerId]);

    private async Task RefreshPreviewManyAsync(IEnumerable<string>? selectedLayerIds = null)
    {
        var selected = new HashSet<string>(selectedLayerIds ?? SelectedLayerIds, StringComparer.Ordinal);
        var bytes = RenderCurrent();
        using var bitmap = Imaging.BitmapCodec.Decode(bytes);
        _imageWidth = bitmap.Width;
        _imageHeight = bitmap.Height;
        ImageSurface.Width = _imageWidth;
        ImageSurface.Height = _imageHeight;
        PreviewImage.Width = _imageWidth;
        PreviewImage.Height = _imageHeight;
        InteractionCanvas.Width = _imageWidth;
        InteractionCanvas.Height = _imageHeight;
        await CaptureOverlayWindow.SetImageAsync(PreviewImage, bytes);
        var displayLayers = _layers.AsEnumerable().Reverse().ToArray();
        LayerList.ItemsSource = displayLayers;
        LayerList.SelectedItems.Clear();
        foreach (var layer in displayLayers)
            if (selected.Contains(layer.Id)) LayerList.SelectedItems.Add(layer);
    }
}
