using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Color;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Magic.Capture.App.Views;

public sealed partial class DesignToolsWindow : Window
{
    private readonly ApplicationServices _services;
    private readonly DispatcherTimer _sampleTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly List<string> _history;
    private readonly List<string> _swatches;
    private ColorValue _current = ColorValue.FromRgb(0, 0, 0);
    private bool _sampling;
    private bool _closed;
    private DateTimeOffset _lastHistoryUiRefresh = DateTimeOffset.MinValue;

    internal DesignToolsWindow(ApplicationServices services)
    {
        InitializeComponent();
        _services = services;
        _history = services.Settings.ColorHistory.ToList();
        _swatches = services.Settings.ColorSwatches.ToList();
        _sampleTimer.Tick += SampleTimer_Tick;
        Activated += OnActivated;
        Closed += OnClosed;
        Platform.WindowHelpers.MoveAndResize(this, 160, 110, 820, 620);
        Platform.WindowHelpers.SetAlwaysOnTop(this, true);
        RefreshLists();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _sampleTimer.Stop();
            return;
        }
        if (!_sampleTimer.IsEnabled) _sampleTimer.Start();
    }

    private async void SampleTimer_Tick(object? sender, object e)
    {
        if (_sampling || _closed) return;
        _sampling = true;
        try { await UpdateLiveSampleAsync(); }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { DesignStatusText.Text = ex.Message; }
        finally { _sampling = false; }
    }

    private async Task UpdateLiveSampleAsync()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var virtualScreen = _services.Monitors.GetVirtualScreenBounds();
        var requested = new PixelRect(cursor.X - 7, cursor.Y - 7, 15, 15);
        var bounds = requested.Intersect(virtualScreen);
        if (bounds.IsEmpty) return;
        var asset = _services.ScreenCapture.Capture(bounds, CaptureSourceKind.Region, "Design picker");
        using var bitmap = BitmapCodec.DecodeForPixelProcessing(asset.PngBytes);
        var sx = Math.Clamp(cursor.X - bounds.X, 0, bitmap.Width - 1);
        var sy = Math.Clamp(cursor.Y - bounds.Y, 0, bitmap.Height - 1);
        var pixel = bitmap.GetPixel(sx, sy);
        _current = ColorValue.FromRgb(pixel.R, pixel.G, pixel.B, pixel.A);
        CurrentSwatch.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(_current.A, _current.R, _current.G, _current.B));
        CurrentColorText.Text = $"{_current.Hex}  {_current.Rgb}\n{_current.Hsl}\n{_current.Hsv}\n{_current.Cmyk}";
        CoordinateText.Text = $"X {cursor.X}  Y {cursor.Y}";
        if (_history.Count == 0 || !string.Equals(_history[0], _current.Hex, StringComparison.OrdinalIgnoreCase))
        {
            _history.Insert(0, _current.Hex);
            if (_history.Count > 32) _history.RemoveRange(32, _history.Count - 32);
            if (DateTimeOffset.UtcNow - _lastHistoryUiRefresh >= TimeSpan.FromMilliseconds(750))
            {
                HistoryList.ItemsSource = null;
                HistoryList.ItemsSource = _history.ToArray();
                _lastHistoryUiRefresh = DateTimeOffset.UtcNow;
            }
        }
        await CaptureOverlayWindow.SetImageAsync(MagnifierImage, asset.PngBytes);
    }

    private void RefreshLists()
    {
        HistoryList.ItemsSource = null; HistoryList.ItemsSource = _history.ToArray();
        SwatchList.ItemsSource = null; SwatchList.ItemsSource = _swatches.ToArray();
    }

    private void SaveSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_swatches.Contains(_current.Hex, StringComparer.OrdinalIgnoreCase)) _swatches.Insert(0, _current.Hex);
        if (_swatches.Count > 24) _swatches.RemoveRange(24, _swatches.Count - 24);
        RefreshLists();
        DesignStatusText.Text = $"Saved {_current.Hex}.";
    }

    private void SampleRegion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!NativeMethods.GetCursorPos(out var cursor)) return;
            var bounds = new PixelRect(cursor.X - 32, cursor.Y - 32, 64, 64).Intersect(_services.Monitors.GetVirtualScreenBounds());
            if (bounds.IsEmpty) return;
            var asset = _services.ScreenCapture.Capture(bounds, CaptureSourceKind.Region, "Design region sample");
            using var bitmap = BitmapCodec.DecodeForPixelProcessing(asset.PngBytes);
            var pixels = BitmapPixelBuffer.ReadBgra(bitmap);
            var palette = ColorPaletteExtractor.ExtractBgra(pixels, bitmap.Width, bitmap.Height, 8);
            RegionStatsText.Text = $"Average {palette.Average.Hex} · dominant {palette.Dominant.Hex} · sampled {palette.SampledPixels:N0} px";
            PaletteText.Text = string.Join(Environment.NewLine, palette.Colors.Select((color, index) => $"{index + 1}. {color.Hex}  {color.Rgb}"));
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { DesignStatusText.Text = ex.Message; }
    }

    private void CopyColorFormat_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag?.ToString();
        var text = tag switch
        {
            "rgb" => _current.Rgb,
            "hsl" => _current.Hsl,
            "hsv" => _current.Hsv,
            "cmyk" => _current.Cmyk,
            "css" => _current.Css,
            "csharp" => _current.CSharp,
            "cpp" => _current.Cpp,
            _ => _current.Hex
        };
        _services.Clipboard.CopyText(text);
        DesignStatusText.Text = $"Copied {text}.";
    }

    private void Contrast_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseHex(ContrastColorBox.Text, out var other)) { ContrastText.Text = "Invalid #RRGGBB"; return; }
        var ratio = ColorContrast.Ratio(_current, other);
        ContrastText.Text = $"{ratio:F2}:1 · {ColorContrast.WcagLabel(ratio)}";
    }

    private void ColorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is not string hex || !TryParseHex(hex, out var color)) return;
        _current = color;
        CurrentSwatch.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(color.A, color.R, color.G, color.B));
        CurrentColorText.Text = $"{color.Hex}  {color.Rgb}\n{color.Hsl}\n{color.Hsv}\n{color.Cmyk}";
    }

    private static bool TryParseHex(string? text, out ColorValue color)
    {
        color = default;
        var raw = text?.Trim().TrimStart('#');
        if (raw is null || raw.Length != 6 || !uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)) return false;
        color = ColorValue.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    private void Ruler_Click(object sender, RoutedEventArgs e) => OpenMeasurementOverlay(MeasurementOverlayMode.Ruler);
    private void Focus_Click(object sender, RoutedEventArgs e) => OpenMeasurementOverlay(MeasurementOverlayMode.Focus);
    private void Whiteboard_Click(object sender, RoutedEventArgs e) => OpenMeasurementOverlay(MeasurementOverlayMode.Whiteboard);

    private void OpenMeasurementOverlay(MeasurementOverlayMode mode)
    {
        var dpi = double.IsFinite(DpiBox.Value) ? Math.Clamp(DpiBox.Value, 10, 2_000) : 96;
        var window = new MeasurementOverlayWindow(_services, mode, dpi);
        ((App)Application.Current).TrackChildWindow(window);
        window.Activate();
    }

    private async void CalibrateDpi_Click(object sender, RoutedEventArgs e)
    {
        var pixels = new NumberBox
        {
            Header = "Measured pixels", Minimum = 1, Maximum = 100_000, Value = 600,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var inches = new NumberBox
        {
            Header = "Known physical length (inches)", Minimum = 0.01, Maximum = 500, Value = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Measure a known object with Pixel ruler, enter its pixel length and real-world length, then apply the calibrated DPI.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(pixels);
        content.Children.Add(inches);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Calibrate screen DPI",
            Content = content,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            DpiBox.Value = ScreenMeasurement.CalibrateDpi(pixels.Value, inches.Value);
            DesignStatusText.Text = $"Calibrated to {DpiBox.Value:F2} DPI.";
        }
        catch (ArgumentOutOfRangeException ex) { DesignStatusText.Text = ex.Message; }
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _sampleTimer.Stop(); _sampleTimer.Tick -= SampleTimer_Tick;
        Activated -= OnActivated; Closed -= OnClosed;
        try
        {
            _ = await ((App)Application.Current).TryMutateSettingsAsync(
                current => current with { ColorHistory = _history.ToArray(), ColorSwatches = _swatches.ToArray() },
                logComponent: "DesignToolsColorsSave");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { _services.Log.Error("DesignToolsSave", ex); }
    }
}
