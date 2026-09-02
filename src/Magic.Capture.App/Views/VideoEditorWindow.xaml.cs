using Magic.Capture.App.Platform;
using Magic.Capture.App.VideoEditing;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.VideoEditing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Storage.Pickers;

namespace Magic.Capture.App.Views;

public sealed partial class VideoEditorWindow : Window
{
    private const int MaximumUndoStates = 32;

    private sealed record TimelineItem(int Index, VideoEditSegment Segment, VideoEditSource? Source)
    {
        public string Display
        {
            get
            {
                if (Segment.IsTitleCard)
                {
                    var text = Segment.TitleCard!.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (text.Length > 48) text = text[..48] + "…";
                    return $"{Index + 1}. Title card · {text} · {Segment.Duration:hh\\:mm\\:ss\\.fff}";
                }

                var name = Source is null ? Segment.SourceId : Path.GetFileName(Source.Path);
                var crop = Segment.Crop is null ? string.Empty : " · crop";
                var mute = Segment.IsMuted ? " · muted" : Segment.Volume == 1.0 ? string.Empty : $" · volume {Segment.Volume * 100:0}%";
                var speed = Math.Abs(Segment.PlaybackRate - 1.0) < 1e-9 ? string.Empty : $" · {Segment.PlaybackRate:0.##}× → {Segment.RenderedDuration:hh\\:mm\\:ss\\.fff}";
                return $"{Index + 1}. {name} · {Segment.SourceStart:hh\\:mm\\:ss\\.fff} → {Segment.SourceEnd:hh\\:mm\\:ss\\.fff} · {Segment.Duration:hh\\:mm\\:ss\\.fff}{speed}{mute}{crop}";
            }
        }

        public override string ToString() => Display;
    }

    private sealed record OverlayItem(int Index, VideoEditOverlay Overlay)
    {
        public string Display
        {
            get
            {
                var tracking = Overlay.Keyframes is { Count: > 1 } ? $" · tracked {Overlay.Keyframes.Count} keyframes" : string.Empty;
                var text = string.IsNullOrWhiteSpace(Overlay.Text) ? string.Empty : $" · {Overlay.Text.Replace('\r', ' ').Replace('\n', ' ').Trim()}";
                return $"{Index + 1}. {Overlay.Kind} · {Overlay.Start:hh\\:mm\\:ss\\.fff} + {Overlay.Duration:hh\\:mm\\:ss\\.fff}{tracking}{text}";
            }
        }

        public override string ToString() => Display;
    }

    private sealed record FrameEffectItem(int Index, VideoEditFrameEffect Effect)
    {
        public override string ToString() => $"{Index + 1}. {Effect.Kind} · {Effect.Start:hh\\:mm\\:ss\\.fff} + {Effect.Duration:hh\\:mm\\:ss\\.fff} · {Effect.Keyframes.Count} keyframe(s)";
    }

    private sealed record OverlayKeyframeItem(int Index, VideoEditOverlayKeyframe Keyframe)
    {
        public override string ToString() => $"{Index + 1}. {Keyframe.Offset:hh\\:mm\\:ss\\.fff} | x {Keyframe.Bounds.X * 100:0.#}% y {Keyframe.Bounds.Y * 100:0.#}% | {Keyframe.Bounds.Width * 100:0.#}x{Keyframe.Bounds.Height * 100:0.#}% | opacity {Keyframe.Opacity * 100:0.#}% | {Keyframe.Easing}";
    }

    private sealed record FrameKeyframeItem(int Index, VideoEditFrameKeyframe Keyframe)
    {
        public override string ToString() => $"{Index + 1}. {Keyframe.Offset:hh\\:mm\\:ss\\.fff} | {Keyframe.Primary:0.###} | x {Keyframe.X * 100:0.#}% y {Keyframe.Y * 100:0.#}% | {Keyframe.Easing}";
    }

    private sealed record AudioKeyframeItem(int Index, VideoEditAudioKeyframe Keyframe)
    {
        public override string ToString() => $"{Index + 1}. {Keyframe.Offset:hh\\:mm\\:ss\\.fff} | gain {Keyframe.Gain * 100:0.#}% | {Keyframe.Easing}";
    }

    private readonly ApplicationServices _services;
    private readonly List<VideoEditProject> _undo = [];
    private readonly List<VideoEditProject> _redo = [];
    private VideoEditProject _project = new([], [], 1920, 1080);
    private string? _projectPath;
    private bool _projectReadOnly;
    private bool _busy;
    private bool _updatingOutputControls;
    private MediaSource? _previewSource;
    private CancellationTokenSource? _operationCts;
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

    internal VideoEditorWindow(ApplicationServices services)
    {
        InitializeComponent();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Closed += VideoEditorWindow_Closed;
        WindowHelpers.MoveAndResize(this, 90, 60, 1420, 900);
        _appWindow = WindowHelpers.GetAppWindow(this);
        _recoveryTimer = DispatcherQueue.CreateTimer();
        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _recoveryTimer.IsRepeating = false;
        _recoveryTimer.Tick += RecoveryTimer_Tick;
        _appWindow.Closing += VideoEditorAppWindow_Closing;
        RefreshTimeline();
        OutputWidthBox.ValueChanged += OutputDimensions_ValueChanged;
        OutputHeightBox.ValueChanged += OutputDimensions_ValueChanged;
        OutputFpsBox.ValueChanged += OutputDimensions_ValueChanged;
    }

    internal VideoEditorWindow(
        VideoEditProjectLoadResult result,
        ApplicationServices services,
        Guid recoverySessionId,
        long dirtyRevision,
        string? displayName)
        : this(services)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Project is null) throw new InvalidDataException("Recovered clip project did not contain a project model.");
        _suppressRecoveryAutosave = true;
        try
        {
            _project = result.Project;
            _projectPath = null;
            _projectReadOnly = result.IsReadOnly;
            _undo.Clear();
            _redo.Clear();
            _recoverySessionId = recoverySessionId == Guid.Empty ? Guid.NewGuid() : recoverySessionId;
            _dirtyRevision = Math.Max(1, dirtyRevision);
            _lastRecoveryRevision = _dirtyRevision;
            _currentProjectDisplayName = displayName;
            SyncOutputBoxes();
            RefreshTimeline(0);
            EditorStatusText.Text = result.Warning ?? "Recovered local video-edit autosave. Save it to choose a destination; no existing project file was overwritten.";
        }
        finally
        {
            _suppressRecoveryAutosave = false;
        }
    }

    private async void AddClips_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _projectReadOnly) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add(".mp4");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;

        try
        {
            SetBusy(true, "Probing clips…");
            var sources = _project.Sources.ToList();
            var segments = _project.Segments.ToList();
            var byPath = sources.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            var firstNewSource = sources.Count == 0;
            foreach (var file in files)
            {
                if (segments.Count >= VideoEditRules.MaximumSegments) throw new InvalidOperationException($"Timeline is limited to {VideoEditRules.MaximumSegments} segments.");
                if (!byPath.TryGetValue(file.Path, out var source))
                {
                    if (sources.Count >= VideoEditRules.MaximumSources) throw new InvalidOperationException($"Project is limited to {VideoEditRules.MaximumSources} source files.");
                    source = await _services.VideoEditComposition.ProbeSourceAsync(file.Path);
                    sources.Add(source);
                    byPath[source.Path] = source;
                }
                segments.Add(new VideoEditSegment(source.Id, TimeSpan.Zero, source.Duration));
            }

            var width = _project.OutputWidth;
            var height = _project.OutputHeight;
            if (firstNewSource && sources.Count > 0)
            {
                width = VideoEditRules.NormalizeOutputDimension(sources[0].Width);
                height = VideoEditRules.NormalizeOutputDimension(sources[0].Height);
            }
            CommitProject(_project with { Sources = sources.ToArray(), Segments = segments.ToArray(), OutputWidth = width, OutputHeight = height }, "Clips added.", segments.Count - 1);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Add clips", ex); }
        finally { SetBusy(false); }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".magicclip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            SetBusy(true, "Opening clip project…");
            var result = await _services.VideoEditProjects.LoadAsync(file.Path);
            if (result.Project is null) throw new InvalidDataException("Clip project did not contain a project model.");
            _suppressRecoveryAutosave = true;
            try
            {
                await InvalidateAndDeleteRecoveryAsync();
                _project = result.Project;
                _projectPath = file.Path;
                _projectReadOnly = result.IsReadOnly;
                _undo.Clear();
                _redo.Clear();
                _currentProjectDisplayName = file.Name;
                _recoverySessionId = Guid.NewGuid();
                _dirtyRevision = 0;
                _lastRecoveryRevision = 0;
                SyncOutputBoxes();
                RefreshTimeline(0);
            }
            finally
            {
                _suppressRecoveryAutosave = false;
            }
            EditorStatusText.Text = result.Warning ?? $"Opened {file.Name}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Open project", ex); }
        finally { SetBusy(false); }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureEditable();
            ApplyOutputDimensions();
            var savedRevision = _dirtyRevision;
            var savedSessionId = _recoverySessionId;
            var savedGeneration = _recoveryGeneration;
            var path = _projectPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "Magic Capture edit" };
                picker.FileTypeChoices.Add("Magic Capture clip project", [".magicclip"]);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file is null) return;
                path = file.Path;
            }
            SetBusy(true, "Saving clip project…");
            await _services.VideoEditProjects.SaveAsync(_project, path);
            _projectPath = path;
            if (savedSessionId == _recoverySessionId && savedGeneration == _recoveryGeneration)
                _currentProjectDisplayName = Path.GetFileName(path);
            await HandleExplicitSaveSucceededAsync(savedSessionId, savedGeneration, savedRevision);
            EditorStatusText.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Save project", ex); }
        finally { SetBusy(false); }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _projectReadOnly || _undo.Count == 0) return;
        PushHistory(_redo, _project);
        _project = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        SyncOutputBoxes();
        RefreshTimeline(Math.Min(TimelineList.SelectedIndex, _project.Segments.Count - 1));
        ScheduleRecoveryAutosave();
        EditorStatusText.Text = "Undo applied.";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _projectReadOnly || _redo.Count == 0) return;
        PushHistory(_undo, _project);
        _project = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        SyncOutputBoxes();
        RefreshTimeline(Math.Min(TimelineList.SelectedIndex, _project.Segments.Count - 1));
        ScheduleRecoveryAutosave();
        EditorStatusText.Text = "Redo applied.";
    }

    private async void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            ApplyOutputDimensions();
            ReplaceOperationToken();
            SetBusy(true, "Building preview…");
            var source = await _services.VideoEditComposition.CreatePreviewSourceAsync(_project, 960, 540, _operationCts!.Token);
            var mediaSource = MediaSource.CreateFromMediaStreamSource(source);
            PreviewPlayer.Source = null;
            _previewSource?.Dispose();
            _previewSource = mediaSource;
            PreviewPlayer.Source = mediaSource;
            EditorStatusText.Text = $"Preview ready · {_project.TimelineDuration:hh\\:mm\\:ss\\.fff}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Preview", ex); }
        finally { SetBusy(false); }
    }

    private async void ExportMp4_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureCurrentSchema();
            ApplyOutputDimensions();
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, SuggestedFileName = "Magic Capture edit" };
            picker.FileTypeChoices.Add("MP4 / H.264", [".mp4"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            ReplaceOperationToken();
            SetBusy(true, "Rendering MP4…", showProgress: true);
            var progress = new Progress<VideoEditRenderProgress>(p =>
            {
                RenderProgressBar.Value = p.Percent;
                EditorStatusText.Text = $"{p.Phase} · {p.Percent:0}%";
            });
            await _services.VideoEditAdvancedRender.RenderMp4Async(_project, file.Path, progress, _operationCts!.Token);
            RenderProgressBar.Value = 100;
            EditorStatusText.Text = $"Rendered {file.Name}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Export MP4", ex); }
        finally { SetBusy(false); }
    }

    private async void CaptureFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureCurrentSchema();
            ApplyOutputDimensions();
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, SuggestedFileName = "Magic Capture frame" };
            picker.FileTypeChoices.Add("PNG image", [".png"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            ReplaceOperationToken();
            SetBusy(true, "Capturing frame…");
            var time = TimeSpan.FromSeconds(Math.Max(0, ValueOr(FrameTimeBox, 0)));
            await _services.VideoEditThumbnails.ExportFramePngAsync(_project, time, file.Path, cancellationToken: _operationCts!.Token);
            EditorStatusText.Text = $"Saved frame {file.Name}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Capture frame", ex); }
        finally { SetBusy(false); }
    }

    private async void ContactSheet_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureCurrentSchema();
            ApplyOutputDimensions();
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, SuggestedFileName = "Magic Capture contact sheet" };
            picker.FileTypeChoices.Add("PNG image", [".png"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            ReplaceOperationToken();
            SetBusy(true, "Building contact sheet…");
            var frames = (int)Math.Round(ValueOr(ContactFramesBox, 16));
            await _services.VideoEditThumbnails.ExportContactSheetPngAsync(_project, file.Path, frames, 320, 180, _operationCts!.Token);
            EditorStatusText.Text = $"Saved contact sheet {file.Name}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Contact sheet", ex); }
        finally { SetBusy(false); }
    }

    private void AddTitleCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (_project.Segments.Count >= VideoEditRules.MaximumSegments)
                throw new InvalidOperationException($"Timeline is limited to {VideoEditRules.MaximumSegments} segments.");
            var text = (TitleCardTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Enter title-card text first.");
            if (text.Length > VideoEditRules.MaximumTitleTextLength) throw new InvalidOperationException("Title-card text is too long.");
            var duration = TimeSpan.FromSeconds(Math.Clamp(ValueOr(TitleCardDurationBox, 2), 0.25, VideoEditRules.MaximumTitleDuration.TotalSeconds));
            var title = new VideoEditTitleCard(text, duration, TextStyle: BuildTextStyle(
                TitleFontFamilyBox, TitleFontWeightCombo, TitleItalicCheck, TitleUnderlineCheck, TitleAlignCombo, TitleShadowBox, TitleOutlineBox));
            var segment = new VideoEditSegment(string.Empty, TimeSpan.Zero, TimeSpan.Zero, Volume: 0, Crop: null, TitleCard: title);
            var segments = _project.Segments.ToList();
            var selected = TimelineList.SelectedIndex;
            var insertAt = selected >= 0 ? Math.Min(selected + 1, segments.Count) : segments.Count;
            segments.Insert(insertAt, segment);
            CommitProject(_project with { Segments = segments.ToArray() }, "Title card added.", insertAt);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Add title card", ex); }
    }

    private void ApplyTitleStyle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            var index = RequireSelectedIndex();
            var segment = _project.Segments[index];
            if (!segment.IsTitleCard) throw new InvalidOperationException("Select a title-card segment first.");
            var text = (TitleCardTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Title-card text cannot be empty.");
            var duration = TimeSpan.FromSeconds(Math.Clamp(ValueOr(TitleCardDurationBox, segment.Duration.TotalSeconds), 0.25, VideoEditRules.MaximumTitleDuration.TotalSeconds));
            var title = segment.TitleCard! with
            {
                Text = text,
                Duration = duration,
                TextStyle = BuildTextStyle(TitleFontFamilyBox, TitleFontWeightCombo, TitleItalicCheck, TitleUnderlineCheck, TitleAlignCombo, TitleShadowBox, TitleOutlineBox)
            };
            var segments = _project.Segments.ToArray();
            segments[index] = segment with { TitleCard = title };
            CommitValidatedProject(_project with { Segments = segments }, "Title card style updated.", index);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Apply title style", ex); }
    }

    private void AddOverlay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (_project.OverlayItems.Count >= VideoEditRules.MaximumOverlays)
                throw new InvalidOperationException($"Project is limited to {VideoEditRules.MaximumOverlays} overlays.");
            if (_project.TimelineDuration <= TimeSpan.Zero) throw new InvalidOperationException("Add timeline content before adding overlays.");

            var kind = OverlayKindCombo.SelectedIndex switch
            {
                0 => VideoEditOverlayKind.Text,
                1 => VideoEditOverlayKind.Rectangle,
                2 => VideoEditOverlayKind.Ellipse,
                3 => VideoEditOverlayKind.Arrow,
                4 => VideoEditOverlayKind.Redaction,
                _ => throw new InvalidOperationException("Select an overlay kind.")
            };
            var startSeconds = Math.Clamp(ValueOr(OverlayStartBox, 0), 0, Math.Max(0, _project.TimelineDuration.TotalSeconds - 0.05));
            var start = TimeSpan.FromSeconds(startSeconds);
            var remaining = _project.TimelineDuration - start;
            var duration = TimeSpan.FromSeconds(Math.Clamp(ValueOr(OverlayDurationBox, 2), 0.05, Math.Max(0.05, remaining.TotalSeconds)));
            if (duration > remaining) duration = remaining;
            var bounds = VideoEditRules.NormalizeCrop(new VideoEditCrop(
                ValueOr(OverlayXBox, 10) / 100.0,
                ValueOr(OverlayYBox, 10) / 100.0,
                ValueOr(OverlayWidthBox, 30) / 100.0,
                ValueOr(OverlayHeightBox, 15) / 100.0));
            var opacity = Math.Clamp(ValueOr(OverlayOpacityBox, 100) / 100.0, 0, 1);
            var stroke = Math.Clamp(ValueOr(OverlayStrokeBox, 3), 0, VideoEditRules.MaximumOverlayStrokeWidth);
            var text = (OverlayTextBox.Text ?? string.Empty).Trim();
            if (kind == VideoEditOverlayKind.Text && string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Text overlays require non-empty text.");

            var (fill, outline) = kind switch
            {
                VideoEditOverlayKind.Text => (0xFFFFFFFFu, 0xFF000000u),
                VideoEditOverlayKind.Rectangle => (0x300078D4u, 0xFF0078D4u),
                VideoEditOverlayKind.Ellipse => (0x300078D4u, 0xFF0078D4u),
                VideoEditOverlayKind.Arrow => (0x00FFFFFFu, 0xFFFFC107u),
                VideoEditOverlayKind.Redaction => (0xFF000000u, 0xFF000000u),
                _ => (0xFFFFFFFFu, 0xFFFFFFFFu)
            };
            var overlay = new VideoEditOverlay(
                Guid.NewGuid().ToString("N"),
                kind,
                start,
                duration,
                bounds,
                opacity,
                fill,
                outline,
                kind == VideoEditOverlayKind.Redaction ? 0 : stroke,
                text,
                TextStyle: kind == VideoEditOverlayKind.Text
                    ? BuildTextStyle(OverlayFontFamilyBox, OverlayFontWeightCombo, OverlayItalicCheck, OverlayUnderlineCheck, OverlayAlignCombo, OverlayShadowBox, OverlayOutlineBox)
                    : null);
            var overlays = _project.OverlayItems.Append(overlay).ToArray();
            CommitProject(_project with { Overlays = overlays }, "Overlay added.", TimelineList.SelectedIndex);
            OverlayList.SelectedIndex = overlays.Length - 1;
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Add overlay", ex); }
    }

    private void OverlayList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshOverlayKeyframeList();

    private void AddOverlayKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (OverlayList.SelectedItem is not OverlayItem selected) throw new InvalidOperationException("Select an overlay first.");
            var overlay = selected.Overlay;
            var offset = TimeSpan.FromSeconds(Math.Clamp(ValueOr(OverlayKeyframeTimeBox, 0), 0, overlay.Duration.TotalSeconds));
            var bounds = VideoEditRules.NormalizeCrop(new VideoEditCrop(
                ValueOr(OverlayKeyframeXBox, overlay.Bounds.X * 100) / 100.0,
                ValueOr(OverlayKeyframeYBox, overlay.Bounds.Y * 100) / 100.0,
                ValueOr(OverlayKeyframeWidthBox, overlay.Bounds.Width * 100) / 100.0,
                ValueOr(OverlayKeyframeHeightBox, overlay.Bounds.Height * 100) / 100.0));
            var keyframe = new VideoEditOverlayKeyframe(
                offset,
                bounds,
                Math.Clamp(ValueOr(OverlayKeyframeOpacityBox, overlay.Opacity * 100) / 100.0, 0.0, 1.0),
                SelectedEasing(OverlayKeyframeEasingCombo));
            var keyframes = (overlay.Keyframes ?? Array.Empty<VideoEditOverlayKeyframe>()).ToList();
            var same = keyframes.FindIndex(x => x.Offset == offset);
            if (same >= 0) keyframes[same] = keyframe; else keyframes.Add(keyframe);
            keyframes = keyframes.OrderBy(x => x.Offset).ToList();
            if (keyframes.Count > VideoEditRules.MaximumTrackingKeyframes)
                throw new InvalidOperationException($"Overlay is limited to {VideoEditRules.MaximumTrackingKeyframes} keyframes.");
            var overlays = _project.OverlayItems.ToArray();
            overlays[selected.Index] = overlay with { Keyframes = keyframes.ToArray() };
            CommitValidatedProject(_project with { Overlays = overlays }, "Overlay keyframe updated.", TimelineList.SelectedIndex);
            OverlayList.SelectedIndex = selected.Index;
            RefreshOverlayKeyframeList();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Overlay keyframe", ex); }
    }

    private void RemoveOverlayKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (OverlayList.SelectedItem is not OverlayItem selected) throw new InvalidOperationException("Select an overlay first.");
            if (OverlayKeyframeList.SelectedItem is not OverlayKeyframeItem key) throw new InvalidOperationException("Select an overlay keyframe first.");
            var list = (selected.Overlay.Keyframes ?? Array.Empty<VideoEditOverlayKeyframe>()).ToList();
            list.RemoveAt(key.Index);
            var overlays = _project.OverlayItems.ToArray();
            overlays[selected.Index] = selected.Overlay with { Keyframes = list.ToArray() };
            CommitValidatedProject(_project with { Overlays = overlays }, "Overlay keyframe removed.", TimelineList.SelectedIndex);
            OverlayList.SelectedIndex = selected.Index;
            RefreshOverlayKeyframeList();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Remove overlay keyframe", ex); }
    }

    private void RemoveOverlay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            var index = OverlayList.SelectedIndex;
            if (index < 0 || index >= _project.OverlayItems.Count) throw new InvalidOperationException("Select an overlay first.");
            var overlays = _project.OverlayItems.ToList();
            overlays.RemoveAt(index);
            CommitProject(_project with { Overlays = overlays.ToArray() }, "Overlay removed.", TimelineList.SelectedIndex);
            OverlayList.SelectedIndex = overlays.Count == 0 ? -1 : Math.Min(index, overlays.Count - 1);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Remove overlay", ex); }
    }

    private async void TrackRedaction_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureEditable();
            var index = OverlayList.SelectedIndex;
            if (index < 0 || index >= _project.OverlayItems.Count) throw new InvalidOperationException("Select a redaction overlay first.");
            var overlay = _project.OverlayItems[index];
            if (overlay.Kind != VideoEditOverlayKind.Redaction) throw new InvalidOperationException("Automatic tracking works only with redaction overlays.");
            ReplaceOperationToken();
            SetBusy(true, "Tracking redaction…", showProgress: true);
            RenderProgressBar.IsIndeterminate = true;
            var update = await _services.VideoEditTracking.TrackRedactionAsync(_project, overlay.Id, TimeSpan.FromMilliseconds(250), _operationCts!.Token);
            RenderProgressBar.IsIndeterminate = false;
            CommitProject(update.Project, $"Tracked redaction · {update.Summary.KeyframeCount} keyframes" + (update.Summary.LostTarget ? " · target lost before end" : string.Empty), TimelineList.SelectedIndex);
            OverlayList.SelectedIndex = index;
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Track redaction", ex); }
        finally
        {
            RenderProgressBar.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private async void ExtractAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureCurrentSchema();
            ApplyOutputDimensions();
            var format = AudioFormatCombo.SelectedIndex switch
            {
                0 => VideoEditAudioFormat.Wav,
                1 => VideoEditAudioFormat.Mp3,
                2 => VideoEditAudioFormat.M4a,
                _ => throw new InvalidOperationException("Select an audio format.")
            };
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.MusicLibrary, SuggestedFileName = "Magic Capture audio" };
            picker.FileTypeChoices.Add(format.ToString(), [VideoEditExportPolicy.Extension(format)]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            ReplaceOperationToken();
            SetBusy(true, "Extracting audio…", showProgress: true);
            var progress = CreateTranscodeProgress();
            await _services.VideoEditTranscode.ExtractAudioAsync(_project, file.Path, format, progress, _operationCts!.Token);
            EditorStatusText.Text = $"Extracted {file.Name}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Extract audio", ex); }
        finally { SetBusy(false); }
    }

    private async void ConvertVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            EnsureCurrentSchema();
            ApplyOutputDimensions();
            var format = VideoFormatCombo.SelectedIndex switch
            {
                0 => VideoEditVideoFormat.H264Mp4,
                1 => VideoEditVideoFormat.HevcMp4,
                2 => VideoEditVideoFormat.Wmv,
                _ => throw new InvalidOperationException("Select a video format.")
            };
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, SuggestedFileName = "Magic Capture converted" };
            picker.FileTypeChoices.Add(format.ToString(), [VideoEditExportPolicy.Extension(format)]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            ReplaceOperationToken();
            SetBusy(true, "Converting video…", showProgress: true);
            var progress = CreateTranscodeProgress();
            await _services.VideoEditTranscode.ConvertVideoAsync(_project, file.Path, format, progress, _operationCts!.Token);
            EditorStatusText.Text = $"Converted {file.Name}.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Convert video", ex); }
        finally { SetBusy(false); }
    }

    private IProgress<VideoEditTranscodeProgress> CreateTranscodeProgress() => new Progress<VideoEditTranscodeProgress>(p =>
    {
        RenderProgressBar.IsIndeterminate = false;
        RenderProgressBar.Value = p.Percent;
        EditorStatusText.Text = $"{p.Phase} · {p.Percent:0}%";
    });

    private void AddAudioKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            var index = RequireSelectedIndex(); var segment = _project.Segments[index];
            if (segment.IsTitleCard) throw new InvalidOperationException("Title cards do not have an audio envelope.");
            var offset = TimeSpan.FromSeconds(Math.Clamp(ValueOr(AudioKeyframeTimeBox, 0), 0, segment.RenderedDuration.TotalSeconds));
            var keyframe = new VideoEditAudioKeyframe(offset, VideoEditRules.NormalizeVolume(ValueOr(AudioKeyframeGainBox, 100) / 100.0), SelectedEasing(AudioKeyframeEasingCombo));
            var list = (segment.AudioEnvelope?.Keyframes ?? Array.Empty<VideoEditAudioKeyframe>()).ToList();
            var same = list.FindIndex(x => x.Offset == offset); if (same >= 0) list[same] = keyframe; else list.Add(keyframe);
            list = list.OrderBy(x => x.Offset).ToList();
            if (list.Count > VideoEditAudioEnvelopePolicy.MaximumKeyframesPerSegment) throw new InvalidOperationException($"Audio envelope is limited to {VideoEditAudioEnvelopePolicy.MaximumKeyframesPerSegment} keyframes.");
            var segments = _project.Segments.ToArray(); segments[index] = segment with { AudioEnvelope = new VideoEditAudioEnvelope(list.ToArray()) };
            CommitValidatedProject(_project with { Segments = segments }, "Audio-envelope keyframe updated.", index);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Audio keyframe", ex); }
    }

    private void RemoveAudioKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable(); var index = RequireSelectedIndex(); var segment = _project.Segments[index];
            if (AudioKeyframeList.SelectedItem is not AudioKeyframeItem key) throw new InvalidOperationException("Select an audio-envelope keyframe first.");
            var list = (segment.AudioEnvelope?.Keyframes ?? Array.Empty<VideoEditAudioKeyframe>()).ToList(); list.RemoveAt(key.Index);
            var segments = _project.Segments.ToArray(); segments[index] = segment with { AudioEnvelope = list.Count == 0 ? null : new VideoEditAudioEnvelope(list.ToArray()) };
            CommitValidatedProject(_project with { Segments = segments }, "Audio-envelope keyframe removed.", index);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Remove audio keyframe", ex); }
    }

    private void ApplyAudioEnvelopePreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable(); var index = RequireSelectedIndex(); var segment = _project.Segments[index];
            if (segment.IsTitleCard) throw new InvalidOperationException("Title cards do not have an audio envelope.");
            var duration = segment.RenderedDuration;
            var fadeIn = TimeSpan.FromSeconds(Math.Clamp(ValueOr(AudioFadeInBox, 0.5), 0, duration.TotalSeconds));
            var fadeOut = TimeSpan.FromSeconds(Math.Clamp(ValueOr(AudioFadeOutBox, 0.5), 0, duration.TotalSeconds));
            TimeSpan? duckStart = null, duckEnd = null;
            if (AudioDuckCheck.IsChecked == true)
            {
                duckStart = TimeSpan.FromSeconds(Math.Clamp(ValueOr(AudioDuckStartBox, 1), 0, duration.TotalSeconds));
                duckEnd = TimeSpan.FromSeconds(Math.Clamp(ValueOr(AudioDuckEndBox, 3), 0, duration.TotalSeconds));
                if (duckEnd <= duckStart) throw new InvalidOperationException("Duck end must be after duck start.");
            }
            var envelope = VideoEditAudioEnvelope.CreateFadeAndDuck(duration, fadeIn, fadeOut, duckStart, duckEnd, ValueOr(AudioDuckGainBox, 35) / 100.0);
            var segments = _project.Segments.ToArray(); segments[index] = segment with { AudioEnvelope = envelope };
            CommitValidatedProject(_project with { Segments = segments }, "Fade / duck envelope applied.", index);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Audio envelope preset", ex); }
    }

    private void ClearAudioEnvelope_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable(); var index = RequireSelectedIndex(); var segments = _project.Segments.ToArray();
            segments[index] = segments[index] with { AudioEnvelope = null };
            CommitValidatedProject(_project with { Segments = segments }, "Audio envelope cleared.", index);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Clear audio envelope", ex); }
    }

    private void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadSelectedControls();

    private void AddFrameEffect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (_project.FrameEffectItems.Count >= VideoEditFrameEffectPolicy.MaximumFrameEffects)
                throw new InvalidOperationException($"Project is limited to {VideoEditFrameEffectPolicy.MaximumFrameEffects} frame effects.");
            var tag = (FrameEffectKindCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? nameof(VideoEditFrameEffectKind.ZoomPan);
            if (!Enum.TryParse<VideoEditFrameEffectKind>(tag, out var kind)) kind = VideoEditFrameEffectKind.ZoomPan;
            var start = TimeSpan.FromSeconds(Math.Max(0, ValueOr(FrameEffectStartBox, 0)));
            var duration = TimeSpan.FromSeconds(Math.Max(0.05, ValueOr(FrameEffectDurationBox, 2)));
            if (start + duration > _project.TimelineDuration) duration = _project.TimelineDuration - start;
            if (duration <= TimeSpan.Zero) throw new InvalidOperationException("Frame effect must start inside the rendered timeline.");
            var p0 = ValueOr(FrameEffectPrimaryStartBox, kind == VideoEditFrameEffectKind.ZoomPan ? 1 : 2);
            var p1 = ValueOr(FrameEffectPrimaryEndBox, p0);
            var x0 = ValueOr(FrameEffectXStartBox, 50) / 100.0;
            var y0 = ValueOr(FrameEffectYStartBox, 50) / 100.0;
            var x1 = ValueOr(FrameEffectXEndBox, 50) / 100.0;
            var y1 = ValueOr(FrameEffectYEndBox, 50) / 100.0;
            var effect = new VideoEditFrameEffect(
                Guid.NewGuid().ToString("N"),
                kind,
                start,
                duration,
                [
                    new VideoEditFrameKeyframe(TimeSpan.Zero, p0, x0, y0, SelectedEasing(FrameEffectEasingCombo)),
                    new VideoEditFrameKeyframe(duration, p1, x1, y1)
                ]);
            var effects = _project.FrameEffectItems.Append(effect).ToArray();
            var next = _project with { FrameEffects = effects };
            var errors = VideoEditRules.ValidateProject(next);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            CommitProject(next, $"Added {kind} frame effect.", TimelineList.SelectedIndex);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Add frame effect", ex); }
    }

    private void FrameEffectList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFrameKeyframeList();

    private void AddFrameKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (FrameEffectList.SelectedItem is not FrameEffectItem selected) throw new InvalidOperationException("Select a frame effect first.");
            var effect = selected.Effect;
            var offset = TimeSpan.FromSeconds(Math.Clamp(ValueOr(FrameKeyframeTimeBox, 0), 0, effect.Duration.TotalSeconds));
            var keyframe = new VideoEditFrameKeyframe(offset, ValueOr(FrameKeyframePrimaryBox, 1), ValueOr(FrameKeyframeXBox, 50) / 100.0, ValueOr(FrameKeyframeYBox, 50) / 100.0, SelectedEasing(FrameKeyframeEasingCombo));
            var list = effect.Keyframes.ToList();
            var same = list.FindIndex(x => x.Offset == offset);
            if (same >= 0) list[same] = keyframe; else list.Add(keyframe);
            list = list.OrderBy(x => x.Offset).ToList();
            if (list.Count > VideoEditFrameEffectPolicy.MaximumKeyframesPerEffect) throw new InvalidOperationException($"Frame effect is limited to {VideoEditFrameEffectPolicy.MaximumKeyframesPerEffect} keyframes.");
            var effects = _project.FrameEffectItems.ToArray();
            effects[selected.Index] = effect with { Keyframes = list.ToArray() };
            CommitValidatedProject(_project with { FrameEffects = effects }, "Frame-effect keyframe updated.", TimelineList.SelectedIndex);
            FrameEffectList.SelectedIndex = selected.Index;
            RefreshFrameKeyframeList();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Frame keyframe", ex); }
    }

    private void RemoveFrameKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (FrameEffectList.SelectedItem is not FrameEffectItem selected) throw new InvalidOperationException("Select a frame effect first.");
            if (FrameKeyframeList.SelectedItem is not FrameKeyframeItem key) throw new InvalidOperationException("Select a frame-effect keyframe first.");
            if (selected.Effect.Keyframes.Count <= 1) throw new InvalidOperationException("A frame effect must keep at least one keyframe.");
            var list = selected.Effect.Keyframes.ToList(); list.RemoveAt(key.Index);
            var effects = _project.FrameEffectItems.ToArray(); effects[selected.Index] = selected.Effect with { Keyframes = list.ToArray() };
            CommitValidatedProject(_project with { FrameEffects = effects }, "Frame-effect keyframe removed.", TimelineList.SelectedIndex);
            FrameEffectList.SelectedIndex = selected.Index;
            RefreshFrameKeyframeList();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Remove frame keyframe", ex); }
    }

    private void RemoveFrameEffect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            if (FrameEffectList.SelectedItem is not FrameEffectItem selected) throw new InvalidOperationException("Select a frame effect first.");
            var effects = _project.FrameEffectItems.Where((_, index) => index != selected.Index).ToArray();
            CommitProject(_project with { FrameEffects = effects }, "Frame effect removed.", TimelineList.SelectedIndex);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Remove frame effect", ex); }
    }

    private void RefreshFrameEffectList()
    {
        var selectedId = FrameEffectList.SelectedItem is FrameEffectItem item ? item.Effect.Id : null;
        var items = _project.FrameEffectItems.Select((effect, index) => new FrameEffectItem(index, effect)).ToArray();
        FrameEffectList.ItemsSource = items;
        if (items.Length == 0) { FrameEffectList.SelectedIndex = -1; return; }
        var restored = string.IsNullOrWhiteSpace(selectedId) ? -1 : Array.FindIndex(items, x => string.Equals(x.Effect.Id, selectedId, StringComparison.Ordinal));
        FrameEffectList.SelectedIndex = restored >= 0 ? restored : Math.Clamp(FrameEffectList.SelectedIndex, 0, items.Length - 1);
        RefreshFrameKeyframeList();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            var index = RequireSelectedIndex();
            if (_project.Segments.Count >= VideoEditRules.MaximumSegments) throw new InvalidOperationException($"Timeline is limited to {VideoEditRules.MaximumSegments} segments.");
            var segments = _project.Segments.ToList();
            segments.Insert(index + 1, segments[index]);
            CommitProject(_project with { Segments = segments.ToArray() }, "Segment duplicated.", index + 1);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Duplicate segment", ex); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            var index = RequireSelectedIndex();
            var segments = _project.Segments.ToList();
            segments.RemoveAt(index);
            CommitProject(_project with { Segments = segments.ToArray() }, "Segment removed.", Math.Min(index, segments.Count - 1));
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Remove segment", ex); }
    }

    private void ApplySegment_Click(object sender, RoutedEventArgs e)
    {
        try { ApplySelectedSegmentFromControls(true); }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Apply segment", ex); }
    }

    private void CutInterval_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureEditable();
            var index = RequireSelectedIndex();
            var segment = _project.Segments[index];
            var cutStart = TimeSpan.FromSeconds(Math.Max(0, ValueOr(CutStartBox, 0)));
            var cutEnd = TimeSpan.FromSeconds(Math.Max(0, ValueOr(CutEndBox, 0)));
            var replacement = VideoEditRules.CutOut(segment, cutStart, cutEnd);
            var segments = _project.Segments.ToList();
            segments.RemoveAt(index);
            segments.InsertRange(index, replacement);
            CommitProject(_project with { Segments = segments.ToArray() }, $"Cut {cutStart:hh\\:mm\\:ss\\.fff}–{cutEnd:hh\\:mm\\:ss\\.fff} from segment.", Math.Min(index, segments.Count - 1));
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Cut interval", ex); }
    }

    private void MoveSelected(int delta)
    {
        try
        {
            EnsureEditable();
            var index = RequireSelectedIndex();
            var target = index + delta;
            if (target < 0 || target >= _project.Segments.Count) return;
            var segments = _project.Segments.ToList();
            (segments[index], segments[target]) = (segments[target], segments[index]);
            CommitProject(_project with { Segments = segments.ToArray() }, "Timeline reordered.", target);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { ShowError("Reorder timeline", ex); }
    }

    private void ApplySelectedSegmentFromControls(bool pushHistory)
    {
        EnsureEditable();
        var index = RequireSelectedIndex();
        var current = _project.Segments[index];
        if (current.IsTitleCard) throw new InvalidOperationException("Title cards do not use source trim/crop/volume controls. Reorder, duplicate, or remove the title card instead.");
        var source = _project.Sources.FirstOrDefault(x => string.Equals(x.Id, current.SourceId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("Selected segment source is missing from the project.");
        var start = TimeSpan.FromSeconds(Math.Max(0, ValueOr(TrimStartBox, current.SourceStart.TotalSeconds)));
        var end = TimeSpan.FromSeconds(Math.Max(0, ValueOr(TrimEndBox, current.SourceEnd.TotalSeconds)));
        var updated = VideoEditRules.Trim(current, start, end, source.Duration);
        var volume = MuteCheck.IsChecked == true ? 0.0 : VideoEditRules.NormalizeVolume(ValueOr(VolumeBox, current.Volume * 100) / 100.0);
        VideoEditCrop? crop = null;
        if (CropEnabledCheck.IsChecked == true)
        {
            crop = VideoEditRules.NormalizeCrop(new VideoEditCrop(
                ValueOr(CropXBox, 0) / 100.0,
                ValueOr(CropYBox, 0) / 100.0,
                ValueOr(CropWidthBox, 100) / 100.0,
                ValueOr(CropHeightBox, 100) / 100.0));
        }
        var playbackRate = VideoEditFrameEffectPolicy.NormalizePlaybackRate(ValueOr(PlaybackRateBox, current.PlaybackRate));
        updated = updated with { Volume = volume, Crop = crop, PlaybackRate = playbackRate };
        var segments = _project.Segments.ToArray();
        segments[index] = updated;
        var next = ApplyOutputDimensions(_project with { Segments = segments });
        if (pushHistory) CommitProject(next, "Segment updated.", index);
        else { _project = next; RefreshTimeline(index); }
    }

    private void OutputDimensions_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_updatingOutputControls) ScheduleRecoveryAutosave();
    }

    private void ApplyOutputDimensions()
    {
        var next = ApplyOutputDimensions(_project);
        var changed = next.OutputWidth != _project.OutputWidth
            || next.OutputHeight != _project.OutputHeight
            || next.OutputFramesPerSecond != _project.OutputFramesPerSecond;
        _project = next;
        if (changed) ScheduleRecoveryAutosave();
    }

    private VideoEditProject ApplyOutputDimensions(VideoEditProject project)
    {
        var width = VideoEditRules.NormalizeOutputDimension((int)Math.Round(ValueOr(OutputWidthBox, project.OutputWidth)));
        var height = VideoEditRules.NormalizeOutputDimension((int)Math.Round(ValueOr(OutputHeightBox, project.OutputHeight)));
        var fps = VideoEditFrameEffectPolicy.NormalizeOutputFps((int)Math.Round(ValueOr(OutputFpsBox, project.OutputFramesPerSecond)));
        _updatingOutputControls = true;
        try
        {
            OutputWidthBox.Value = width;
            OutputHeightBox.Value = height;
            OutputFpsBox.Value = fps;
        }
        finally
        {
            _updatingOutputControls = false;
        }
        return project with { OutputWidth = width, OutputHeight = height, OutputFramesPerSecond = fps };
    }

    private void CommitProject(VideoEditProject next, string status, int selectedIndex)
    {
        if (_projectReadOnly) throw new InvalidOperationException("This project uses a future schema and is open read-only.");
        PushHistory(_undo, _project);
        _redo.Clear();
        _project = next;
        SyncOutputBoxes();
        RefreshTimeline(selectedIndex);
        ScheduleRecoveryAutosave();
        EditorStatusText.Text = status + " Refresh preview to inspect the non-destructive result.";
    }

    private void RefreshTimeline(int selectedIndex = -1)
    {
        var sources = _project.Sources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var items = _project.Segments.Select((segment, index) => new TimelineItem(index, segment, sources.GetValueOrDefault(segment.SourceId))).ToArray();
        TimelineList.ItemsSource = items;
        if (items.Length > 0)
        {
            TimelineList.SelectedIndex = selectedIndex < 0 ? Math.Clamp(TimelineList.SelectedIndex, 0, items.Length - 1) : Math.Clamp(selectedIndex, 0, items.Length - 1);
            if (TimelineList.SelectedIndex < 0) TimelineList.SelectedIndex = 0;
        }
        else TimelineList.SelectedIndex = -1;
        TimelineSummaryText.Text = $"{items.Length} segment(s) · {_project.Sources.Count} source(s) · {_project.OverlayItems.Count} overlay(s) · {_project.FrameEffectItems.Count} frame effect(s) · {_project.TimelineDuration:hh\\:mm\\:ss\\.fff} · output {_project.OutputWidth}×{_project.OutputHeight}@{_project.OutputFramesPerSecond}" + (_projectReadOnly ? " · READ-ONLY future schema" : string.Empty);
        RefreshOverlayList();
        RefreshFrameEffectList();
        LoadSelectedControls();
    }

    private void LoadSelectedControls()
    {
        if (TimelineList.SelectedIndex < 0 || TimelineList.SelectedIndex >= _project.Segments.Count)
        {
            SelectedSourceText.Text = "Select a timeline segment.";
            return;
        }
        var segment = _project.Segments[TimelineList.SelectedIndex];
        var source = segment.IsTitleCard ? null : _project.Sources.FirstOrDefault(x => string.Equals(x.Id, segment.SourceId, StringComparison.Ordinal));
        SelectedSourceText.Text = segment.IsTitleCard
            ? $"Title card · {segment.TitleCard!.Duration:hh\\:mm\\:ss\\.fff} · {segment.TitleCard.Text}"
            : source is null
                ? $"Missing source id: {segment.SourceId}"
                : $"{Path.GetFileName(source.Path)} · source {source.Duration:hh\\:mm\\:ss\\.fff} · {source.Width}×{source.Height}";
        var sourceControlsEnabled = !segment.IsTitleCard;
        TrimStartBox.IsEnabled = sourceControlsEnabled;
        TrimEndBox.IsEnabled = sourceControlsEnabled;
        VolumeBox.IsEnabled = sourceControlsEnabled;
        PlaybackRateBox.IsEnabled = sourceControlsEnabled;
        MuteCheck.IsEnabled = sourceControlsEnabled;
        CutStartBox.IsEnabled = sourceControlsEnabled;
        CutEndBox.IsEnabled = sourceControlsEnabled;
        CropEnabledCheck.IsEnabled = sourceControlsEnabled;
        CropXBox.IsEnabled = sourceControlsEnabled;
        CropYBox.IsEnabled = sourceControlsEnabled;
        CropWidthBox.IsEnabled = sourceControlsEnabled;
        CropHeightBox.IsEnabled = sourceControlsEnabled;
        TrimStartBox.Value = segment.IsTitleCard ? 0 : segment.SourceStart.TotalSeconds;
        TrimEndBox.Value = segment.IsTitleCard ? segment.Duration.TotalSeconds : segment.SourceEnd.TotalSeconds;
        VolumeBox.Value = segment.Volume * 100.0;
        PlaybackRateBox.Value = segment.PlaybackRate;
        MuteCheck.IsChecked = segment.IsMuted;
        CropEnabledCheck.IsChecked = segment.Crop is not null;
        var crop = segment.Crop ?? new VideoEditCrop(0, 0, 1, 1);
        CropXBox.Value = crop.X * 100.0;
        CropYBox.Value = crop.Y * 100.0;
        CropWidthBox.Value = crop.Width * 100.0;
        CropHeightBox.Value = crop.Height * 100.0;
        CutStartBox.Value = 0;
        CutEndBox.Value = Math.Min(1.0, segment.Duration.TotalSeconds);
        if (segment.IsTitleCard)
        {
            TitleCardTextBox.Text = segment.TitleCard!.Text;
            TitleCardDurationBox.Value = segment.TitleCard.Duration.TotalSeconds;
            LoadTextStyle(segment.TitleCard.TextStyle, TitleFontFamilyBox, TitleFontWeightCombo, TitleItalicCheck, TitleUnderlineCheck, TitleAlignCombo, TitleShadowBox, TitleOutlineBox);
        }
        RefreshAudioKeyframeList();
    }

    private void RefreshOverlayList()
    {
        var selectedId = OverlayList.SelectedItem is OverlayItem item ? item.Overlay.Id : null;
        var items = _project.OverlayItems.Select((overlay, index) => new OverlayItem(index, overlay)).ToArray();
        OverlayList.ItemsSource = items;
        if (items.Length == 0)
        {
            OverlayList.SelectedIndex = -1;
            RefreshOverlayKeyframeList();
            return;
        }
        var restored = string.IsNullOrWhiteSpace(selectedId)
            ? -1
            : Array.FindIndex(items, x => string.Equals(x.Overlay.Id, selectedId, StringComparison.Ordinal));
        OverlayList.SelectedIndex = restored >= 0 ? restored : Math.Clamp(OverlayList.SelectedIndex, 0, items.Length - 1);
        RefreshOverlayKeyframeList();
    }

    private void RefreshOverlayKeyframeList()
    {
        var items = OverlayList.SelectedItem is OverlayItem selected
            ? (selected.Overlay.Keyframes ?? Array.Empty<VideoEditOverlayKeyframe>()).Select((keyframe, index) => new OverlayKeyframeItem(index, keyframe)).ToArray()
            : Array.Empty<OverlayKeyframeItem>();
        OverlayKeyframeList.ItemsSource = items;
        if (items.Length == 0) OverlayKeyframeList.SelectedIndex = -1;
    }

    private void RefreshFrameKeyframeList()
    {
        var items = FrameEffectList.SelectedItem is FrameEffectItem selected
            ? selected.Effect.Keyframes.Select((keyframe, index) => new FrameKeyframeItem(index, keyframe)).ToArray()
            : Array.Empty<FrameKeyframeItem>();
        FrameKeyframeList.ItemsSource = items;
        if (items.Length == 0) FrameKeyframeList.SelectedIndex = -1;
    }

    private void RefreshAudioKeyframeList()
    {
        var segment = TimelineList.SelectedIndex >= 0 && TimelineList.SelectedIndex < _project.Segments.Count ? _project.Segments[TimelineList.SelectedIndex] : null;
        var items = (segment?.AudioEnvelope?.Keyframes ?? Array.Empty<VideoEditAudioKeyframe>()).Select((keyframe, index) => new AudioKeyframeItem(index, keyframe)).ToArray();
        AudioKeyframeList.ItemsSource = items;
        if (items.Length == 0) AudioKeyframeList.SelectedIndex = -1;
        var enabled = segment is { IsTitleCard: false };
        AudioKeyframeTimeBox.IsEnabled = enabled; AudioKeyframeGainBox.IsEnabled = enabled; AudioKeyframeEasingCombo.IsEnabled = enabled;
        AudioFadeInBox.IsEnabled = enabled; AudioFadeOutBox.IsEnabled = enabled; AudioDuckCheck.IsEnabled = enabled; AudioDuckStartBox.IsEnabled = enabled; AudioDuckEndBox.IsEnabled = enabled; AudioDuckGainBox.IsEnabled = enabled;
    }

    private void CommitValidatedProject(VideoEditProject next, string status, int selectedIndex)
    {
        var errors = VideoEditRules.ValidateProject(next);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        CommitProject(next, status, selectedIndex);
    }

    private static VideoEditEasingKind SelectedEasing(ComboBox combo)
    {
        var tag = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        return Enum.TryParse<VideoEditEasingKind>(tag, out var value) ? value : VideoEditEasingKind.Linear;
    }

    private static VideoEditTextStyle BuildTextStyle(TextBox familyBox, ComboBox weightCombo, CheckBox italicCheck, CheckBox underlineCheck, ComboBox alignCombo, NumberBox shadowBox, NumberBox outlineBox)
    {
        var weightTag = (weightCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        _ = int.TryParse(weightTag, out var weight); if (weight == 0) weight = 700;
        var alignTag = (alignCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var align = Enum.TryParse<VideoEditTextAlignment>(alignTag, out var parsed) ? parsed : VideoEditTextAlignment.Center;
        return VideoEditTextStyle.Normalize(new VideoEditTextStyle(familyBox.Text ?? "Segoe UI", weight, italicCheck.IsChecked == true, underlineCheck.IsChecked == true, align, ShadowArgb: 0x88000000u, ShadowOffset: ValueOr(shadowBox, 0), OutlineArgb: 0xFF000000u, OutlineWidth: ValueOr(outlineBox, 0)));
    }

    private static void LoadTextStyle(VideoEditTextStyle? style, TextBox familyBox, ComboBox weightCombo, CheckBox italicCheck, CheckBox underlineCheck, ComboBox alignCombo, NumberBox shadowBox, NumberBox outlineBox)
    {
        var value = VideoEditTextStyle.Normalize(style); familyBox.Text = value.FontFamily;
        weightCombo.SelectedIndex = value.Weight switch { <= 400 => 0, <= 600 => 1, <= 700 => 2, _ => 3 };
        italicCheck.IsChecked = value.Italic; underlineCheck.IsChecked = value.Underline;
        alignCombo.SelectedIndex = value.HorizontalAlignment switch { VideoEditTextAlignment.Left => 0, VideoEditTextAlignment.Right => 2, _ => 1 };
        shadowBox.Value = value.ShadowOffset; outlineBox.Value = value.OutlineWidth;
    }

    private int RequireSelectedIndex()
    {
        var index = TimelineList.SelectedIndex;
        if (index < 0 || index >= _project.Segments.Count) throw new InvalidOperationException("Select a timeline segment first.");
        return index;
    }

    private void EnsureEditable()
    {
        if (_projectReadOnly) throw new InvalidOperationException("This project uses a future schema and is open read-only.");
        if (_busy) throw new InvalidOperationException("Wait for the current editor operation to finish.");
    }

    private void EnsureCurrentSchema()
    {
        if (!VideoEditProjectSchema.CanWrite(_project.SchemaVersion)) throw new InvalidOperationException("Future-schema clip projects cannot be previewed or rendered by this version.");
        var errors = VideoEditRules.ValidateProject(_project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
    }

    private void SyncOutputBoxes()
    {
        _updatingOutputControls = true;
        try
        {
            OutputWidthBox.Value = _project.OutputWidth;
            OutputHeightBox.Value = _project.OutputHeight;
            OutputFpsBox.Value = _project.OutputFramesPerSecond;
        }
        finally
        {
            _updatingOutputControls = false;
        }
    }

    private static void PushHistory(List<VideoEditProject> history, VideoEditProject project)
    {
        history.Add(project);
        if (history.Count > MaximumUndoStates) history.RemoveAt(0);
    }

    private void ScheduleRecoveryAutosave()
    {
        if (_suppressRecoveryAutosave || _closingCleanly || _projectReadOnly || !VideoEditProjectSchema.CanWrite(_project.SchemaVersion)) return;
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
        if (_suppressRecoveryAutosave || _closingCleanly || _projectReadOnly || !VideoEditProjectSchema.CanWrite(_project.SchemaVersion)
            || _dirtyRevision <= _lastRecoveryRevision) return;
        var revision = _dirtyRevision;
        var sessionId = _recoverySessionId;
        var generation = _recoveryGeneration;
        var project = ApplyOutputDimensions(_project);
        var displayName = _currentProjectDisplayName ?? "Unsaved video edit";

        await _recoveryWriteGate.WaitAsync();
        try
        {
            if (_closingCleanly || generation != _recoveryGeneration || sessionId != _recoverySessionId || revision <= _lastRecoveryRevision) return;
            await _services.VideoEditRecovery.SaveAsync(sessionId, project, revision, displayName);
            if (generation != _recoveryGeneration || sessionId != _recoverySessionId) return;
            _lastRecoveryRevision = revision;
            if (!_closingCleanly && _dirtyRevision == revision) EditorStatusText.Text = "Autosaved video-edit recovery.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("VideoEditRecoveryAutosave", ex);
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
                await _services.VideoEditRecovery.DeleteAsync(savedSessionId);
            }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
            {
                _services.Log.Error("VideoEditRecoverySaveCleanup", ex);
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
            await _services.VideoEditRecovery.DeleteAsync(sessionId);
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

    private async void VideoEditorAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
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
            _services.Log.Error("VideoEditRecoveryClose", ex);
        }
        finally
        {
            _closeCleanupComplete = true;
            _closingCleanly = false;
        }
        if ((Application.Current as App)?.IsExitRequested != true) Close();
    }

    private void ReplaceOperationToken()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    private void SetBusy(bool busy, string? status = null, bool showProgress = false)
    {
        _busy = busy;
        RenderProgressBar.Visibility = showProgress && busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            RenderProgressBar.Value = 0;
            RenderProgressBar.IsIndeterminate = false;
        }
        if (!string.IsNullOrWhiteSpace(status)) EditorStatusText.Text = status;
    }

    private void ShowError(string component, Exception ex)
    {
        _services.Log.Error("VideoEditor." + component, ex);
        EditorStatusText.Text = ex.Message;
        RenderProgressBar.Visibility = Visibility.Collapsed;
    }

    private static double ValueOr(NumberBox box, double fallback) => double.IsFinite(box.Value) ? box.Value : fallback;

    private void VideoEditorWindow_Closed(object sender, WindowEventArgs args)
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = null;
        PreviewPlayer.Source = null;
        _previewSource?.Dispose();
        _previewSource = null;
    }
}
