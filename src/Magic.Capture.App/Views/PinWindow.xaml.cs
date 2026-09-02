using System.Runtime.InteropServices;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Geometry;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Magic.Capture.App.Views;

public sealed partial class PinWindow : Window
{
    private readonly CaptureAsset _asset;
    private readonly ApplicationServices _services;
    private readonly NativeMessageRouter _nativeRouter = new();
    private bool _initialized;
    private double _aspectRatio = 1.0;
    private readonly bool _allowClickThrough;
    private bool _clickThrough;
    private int _imageWidth;
    private int _imageHeight;
    private int _initialWidth;
    private int _initialHeight;
    private double _zoom = 1.0;
    private bool _fitMode = true;
    private bool _positionLocked;
    private PixelRect _lockedRect;
    private bool _edgeHidden;
    private PixelRect _edgeRestoreRect;
    private PinAnnotationMode _annotationMode;
    private string? _pendingNoteText;
    private int _nextStepNumber = 1;
    private readonly List<PinAnnotationItem> _pinAnnotations = [];

    internal PinWindow(CaptureAsset asset, ApplicationServices services, bool allowClickThrough)
    {
        InitializeComponent();
        _asset = asset;
        _services = services;
        _allowClickThrough = allowClickThrough;
        ClickThroughButton.IsEnabled = allowClickThrough;
        Activated += OnActivated;
        Closed += OnClosed;
        _nativeRouter.MessageReceived += OnNativeMessage;
        Root.SizeChanged += Root_SizeChanged;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized || args.WindowActivationState == WindowActivationState.Deactivated) return;
        _initialized = true;

        using var bitmap = BitmapCodec.Decode(_asset.PngBytes);
        _imageWidth = bitmap.Width;
        _imageHeight = bitmap.Height;
        _aspectRatio = _imageWidth / (double)Math.Max(1, _imageHeight);
        (_initialWidth, _initialHeight) = FitInitialSize(_imageWidth, _imageHeight);
        var startX = _services.Settings.PinLastX ?? 180;
        var startY = _services.Settings.PinLastY ?? 140;
        var startWidth = _services.Settings.PinLastWidth ?? _initialWidth;
        var startHeight = _services.Settings.PinLastHeight ?? _initialHeight;
        WindowHelpers.MoveAndResize(this, startX, startY, startWidth, startHeight);

        if (WindowHelpers.GetAppWindow(this).Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }

        _nativeRouter.Attach(WindowHelpers.GetWindowHandle(this));
        WindowHelpers.SetOpacity(this, _services.Settings.PinOpacity);
        await CaptureOverlayWindow.SetImageAsync(PinnedImage, _asset.PngBytes);
        SetFitView();
    }

    private static (int Width, int Height) FitInitialSize(int imageWidth, int imageHeight)
    {
        const int maxWidth = 760;
        const int maxHeight = 620;
        const int minWidth = 240;
        const int minHeight = 160;

        var ratio = imageWidth / (double)Math.Max(1, imageHeight);
        var width = Math.Min(maxWidth, imageWidth);
        var height = Math.Max(1, (int)Math.Round(width / ratio));

        if (height > maxHeight)
        {
            height = maxHeight;
            width = Math.Max(1, (int)Math.Round(height * ratio));
        }
        if (width < minWidth)
        {
            width = minWidth;
            height = Math.Max(1, (int)Math.Round(width / ratio));
        }
        if (height < minHeight)
        {
            height = minHeight;
            width = Math.Max(1, (int)Math.Round(height * ratio));
        }
        return (width, height);
    }

    private void OnNativeMessage(object? sender, NativeWindowMessage message)
    {
        if (message.LParam == IntPtr.Zero) return;
        if (_positionLocked && message.Message is NativeConstants.WmMoving or NativeConstants.WmSizing)
        {
            var locked = new NativeRect { Left = _lockedRect.X, Top = _lockedRect.Y, Right = _lockedRect.Right, Bottom = _lockedRect.Bottom };
            Marshal.StructureToPtr(locked, message.LParam, false);
            return;
        }
        if (message.Message != NativeConstants.WmSizing) return;
        var native = Marshal.PtrToStructure<NativeRect>(message.LParam);
        var proposed = new PixelRect(native.Left, native.Top, Math.Max(1, native.Right - native.Left), Math.Max(1, native.Bottom - native.Top));
        var edgeValue = (uint)message.WParam.ToUInt64();
        if (!Enum.IsDefined(typeof(ResizeEdge), edgeValue)) return;
        var constrained = AspectRatioResize.Constrain(proposed, (ResizeEdge)edgeValue, _aspectRatio, 160, 100);
        native.Left = constrained.X;
        native.Top = constrained.Y;
        native.Right = constrained.Right;
        native.Bottom = constrained.Bottom;
        Marshal.StructureToPtr(native, message.LParam, false);
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_clickThrough) return;
        PinControls.Opacity = 1;
        PinControls.IsHitTestVisible = true;
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        PinControls.Opacity = 0;
        PinControls.IsHitTestVisible = false;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            var bounds = _edgeHidden && !_edgeRestoreRect.IsEmpty ? _edgeRestoreRect : GetWindowBounds();
            _ = await ((App)Application.Current).TryMutateSettingsAsync(
                current => current with { PinLastX = bounds.X, PinLastY = bounds.Y, PinLastWidth = bounds.Width, PinLastHeight = bounds.Height },
                logComponent: "PinGeometrySave");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _services.Log.Error("PinGeometrySave", ex);
        }
        Root.SizeChanged -= Root_SizeChanged;
        _nativeRouter.MessageReceived -= OnNativeMessage;
        _nativeRouter.Dispose();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode) ApplyFitDimensions();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void Fit_Click(object sender, RoutedEventArgs e) => SetFitView();
    private void ActualSize_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SetFitView();
        var appWindow = WindowHelpers.GetAppWindow(this);
        WindowHelpers.MoveAndResize(this, appWindow.Position.X, appWindow.Position.Y, _initialWidth, _initialHeight);
        PinStatusText.Text = "Reset";
    }

    private void SetZoom(double value)
    {
        _fitMode = false;
        var maximumScale = Math.Min(4.0, 16_384d / Math.Max(1, Math.Max(_imageWidth, _imageHeight)));
        _zoom = Math.Clamp(value, 0.25, Math.Max(0.25, maximumScale));
        PinnedImage.Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill;
        PinnedImage.HorizontalAlignment = HorizontalAlignment.Center;
        PinnedImage.VerticalAlignment = VerticalAlignment.Center;
        PinnedImage.Width = Math.Max(1, _imageWidth * _zoom);
        PinnedImage.Height = Math.Max(1, _imageHeight * _zoom);
        SyncImageHostAndAnnotations();
        PinStatusText.Text = $"{_zoom * 100:F0}%";
    }

    private void SetFitView()
    {
        _fitMode = true;
        _zoom = 1.0;
        PinnedImage.Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill;
        PinnedImage.HorizontalAlignment = HorizontalAlignment.Center;
        PinnedImage.VerticalAlignment = VerticalAlignment.Center;
        ApplyFitDimensions();
        PinStatusText.Text = "Fit";
    }

    private void ApplyFitDimensions()
    {
        var availableWidth = Math.Max(1d, Root.ActualWidth > 1 ? Root.ActualWidth : _initialWidth);
        var availableHeight = Math.Max(1d, Root.ActualHeight > 1 ? Root.ActualHeight : _initialHeight);
        var scale = Math.Min(availableWidth / Math.Max(1, _imageWidth), availableHeight / Math.Max(1, _imageHeight));
        scale = Math.Max(0.01, scale);
        PinnedImage.Width = Math.Max(1, _imageWidth * scale);
        PinnedImage.Height = Math.Max(1, _imageHeight * scale);
        SyncImageHostAndAnnotations();
    }

    private enum PinAnnotationMode { None, Step, Note }
    private sealed record PinAnnotationItem(PinAnnotationMode Kind, double X, double Y, string Text);

    private void SyncImageHostAndAnnotations()
    {
        var width = Math.Max(1, PinnedImage.Width);
        var height = Math.Max(1, PinnedImage.Height);
        ImageHost.Width = width;
        ImageHost.Height = height;
        PinAnnotationCanvas.Width = width;
        PinAnnotationCanvas.Height = height;
        RenderPinAnnotations();
    }

    private void RenderPinAnnotations()
    {
        PinAnnotationCanvas.Children.Clear();
        var width = Math.Max(1, PinAnnotationCanvas.Width);
        var height = Math.Max(1, PinAnnotationCanvas.Height);
        foreach (var item in _pinAnnotations)
        {
            var x = Math.Clamp(item.X * width, 0, width);
            var y = Math.Clamp(item.Y * height, 0, height);
            if (item.Kind == PinAnnotationMode.Step)
            {
                var marker = new Grid { Width = 30, Height = 30 };
                marker.Children.Add(new Ellipse { Fill = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), StrokeThickness = 2 });
                marker.Children.Add(new TextBlock { Text = item.Text, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                Canvas.SetLeft(marker, x - 15); Canvas.SetTop(marker, y - 15); PinAnnotationCanvas.Children.Add(marker);
            }
            else if (item.Kind == PinAnnotationMode.Note)
            {
                var note = new Border { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(224, 255, 244, 180)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6), MaxWidth = 260 };
                note.Child = new TextBlock { Text = item.Text, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black), TextWrapping = TextWrapping.Wrap };
                Canvas.SetLeft(note, x); Canvas.SetTop(note, y); PinAnnotationCanvas.Children.Add(note);
            }
        }
    }

    private void ImageHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_annotationMode == PinAnnotationMode.None || ImageHost.ActualWidth <= 1 || ImageHost.ActualHeight <= 1) return;
        var point = e.GetCurrentPoint(ImageHost).Position;
        var nx = Math.Clamp(point.X / ImageHost.ActualWidth, 0, 1);
        var ny = Math.Clamp(point.Y / ImageHost.ActualHeight, 0, 1);
        if (_annotationMode == PinAnnotationMode.Step)
            _pinAnnotations.Add(new PinAnnotationItem(PinAnnotationMode.Step, nx, ny, (_nextStepNumber++).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        else if (_annotationMode == PinAnnotationMode.Note && !string.IsNullOrWhiteSpace(_pendingNoteText))
            _pinAnnotations.Add(new PinAnnotationItem(PinAnnotationMode.Note, nx, ny, _pendingNoteText));
        _annotationMode = PinAnnotationMode.None;
        _pendingNoteText = null;
        RenderPinAnnotations();
        PinStatusText.Text = "Pin annotation placed";
        e.Handled = true;
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        _annotationMode = PinAnnotationMode.Step;
        _pendingNoteText = null;
        PinStatusText.Text = "Click image to place step";
    }

    private async void Note_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { PlaceholderText = "Note", MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 280 };
        var dialog = new ContentDialog { Title = "Pin note", Content = input, PrimaryButtonText = "Place", CloseButtonText = "Cancel", XamlRoot = Root.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;
        _pendingNoteText = input.Text.Trim();
        _annotationMode = PinAnnotationMode.Note;
        PinStatusText.Text = "Click image to place note";
    }

    private void ClearMarks_Click(object sender, RoutedEventArgs e)
    {
        _pinAnnotations.Clear(); _annotationMode = PinAnnotationMode.None; _pendingNoteText = null; _nextStepNumber = 1; RenderPinAnnotations(); PinStatusText.Text = "Pin annotations cleared";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowHelpers.GetAppWindow(this).Presenter is OverlappedPresenter presenter) presenter.Minimize();
    }

    private void HideEdge_Click(object sender, RoutedEventArgs e)
    {
        var appWindow = WindowHelpers.GetAppWindow(this);
        if (!_edgeHidden)
        {
            _edgeRestoreRect = GetWindowBounds();
            var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            var visible = 22;
            appWindow.Move(new Windows.Graphics.PointInt32(work.X + work.Width - visible, Math.Clamp(appWindow.Position.Y, work.Y, work.Y + Math.Max(0, work.Height - appWindow.Size.Height))));
            _edgeHidden = true; EdgeButton.Content = "Restore"; PinStatusText.Text = "Hidden to screen edge";
        }
        else
        {
            WindowHelpers.MoveAndResize(this, _edgeRestoreRect.X, _edgeRestoreRect.Y, _edgeRestoreRect.Width, _edgeRestoreRect.Height);
            _edgeHidden = false; EdgeButton.Content = "Edge"; PinStatusText.Text = "Restored";
        }
    }

    private void LockPosition_Click(object sender, RoutedEventArgs e)
    {
        _positionLocked = !_positionLocked;
        _lockedRect = GetWindowBounds();
        if (WindowHelpers.GetAppWindow(this).Presenter is OverlappedPresenter presenter) presenter.IsResizable = !_positionLocked;
        LockButton.Content = _positionLocked ? "Unlock" : "Lock";
        PinStatusText.Text = _positionLocked ? "Position locked" : "Position unlocked";
    }

    private void ArrangePins_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ArrangePinsGrid();
    private void SnapPins_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).SnapPins();

    internal PixelRect GetWindowBounds()
    {
        var app = WindowHelpers.GetAppWindow(this);
        return new PixelRect(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);
    }

    internal void MovePin(int x, int y)
    {
        if (_positionLocked) return;
        WindowHelpers.GetAppWindow(this).Move(new Windows.Graphics.PointInt32(x, y));
    }

    private async Task SetOpacityAndPersistAsync(double value)
    {
        value = Math.Clamp(value, 0.5, 1.0);
        WindowHelpers.SetOpacity(this, value);
        try
        {
            var saved = await ((App)Application.Current).TryMutateSettingsAsync(
                current => current with { PinOpacity = value },
                logComponent: "PinOpacitySave");
            PinStatusText.Text = saved
                ? $"Opacity {value * 100:F0}%"
                : $"Opacity {value * 100:F0}% · session only";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            PinStatusText.Text = "Opacity changed · save failed";
            _services.Log.Error("PinOpacitySave", ex);
        }
    }

    private async void Opacity100_Click(object sender, RoutedEventArgs e) => await SetOpacityAndPersistAsync(1.0);
    private async void Opacity90_Click(object sender, RoutedEventArgs e) => await SetOpacityAndPersistAsync(0.9);
    private async void Opacity75_Click(object sender, RoutedEventArgs e) => await SetOpacityAndPersistAsync(0.75);
    private async void Opacity50_Click(object sender, RoutedEventArgs e) => await SetOpacityAndPersistAsync(0.5);

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { await _services.Clipboard.CopyImageAsync(_asset.PngBytes); PinStatusText.Text = "Copied"; }
        catch (Exception ex) { _services.Log.Error("PinCopy", ex); PinStatusText.Text = "Copy failed"; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = await _services.Export.SaveImageAsAsync(this, _asset, "png", _services.Settings.JpegQuality, _services.Settings.FileNameTemplate);
            PinStatusText.Text = saved is null ? "Save cancelled" : "Saved";
        }
        catch (Exception ex) { _services.Log.Error("PinSave", ex); PinStatusText.Text = "Save failed"; }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).OpenAnnotation(_asset);
        PinStatusText.Text = "Opened editor";
    }

    private void ClickThrough_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowClickThrough) return;
        SetClickThrough(!_clickThrough);
    }

    internal void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled && _allowClickThrough;
        WindowHelpers.SetClickThrough(this, _clickThrough);
        ClickThroughButton.Content = _clickThrough ? "Click-through on · tray to restore" : "Click-through · PRO";
        if (_clickThrough)
        {
            PinControls.Opacity = 0;
            PinControls.IsHitTestVisible = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
