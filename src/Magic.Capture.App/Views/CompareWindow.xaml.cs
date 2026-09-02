using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Commerce;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;

namespace Magic.Capture.App.Views;

public sealed partial class CompareWindow : Window
{
    private readonly byte[] _first;
    private readonly byte[] _second;
    private readonly ApplicationServices _services;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private readonly DispatcherTimer _compareDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private ImageCompareResult? _result;
    private bool _initialized;
    private bool _autoAlignTranslation;
    private bool _autoRegisterContent;
    private CompareSemanticAnalysisResult? _semanticResult;
    private CancellationTokenSource? _semanticCts;
    private bool _blinkShowingSecond = true;
    private int _renderGeneration;
    private CancellationTokenSource? _recomputeCts;
    private bool _closed;

    internal CompareWindow(byte[] first, string firstName, byte[] second, string secondName, ApplicationServices services)
    {
        InitializeComponent();
        _first = first;
        _second = second;
        _services = services;
        FirstNameText.Text = firstName;
        SecondNameText.Text = secondName;
        _blinkTimer.Tick += BlinkTimer_Tick;
        _compareDebounceTimer.Tick += CompareDebounceTimer_Tick;
        Closed += OnClosed;
        Platform.WindowHelpers.MoveAndResize(this, 120, 80, 1200, 820);
        Activated += OnActivated;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized || args.WindowActivationState == WindowActivationState.Deactivated) return;
        _initialized = true;
        try
        {
            await Task.WhenAll(
                CaptureOverlayWindow.SetImageAsync(FirstImage, _first),
                CaptureOverlayWindow.SetImageAsync(SecondImage, _second),
                CaptureOverlayWindow.SetImageAsync(OverlayFirstImage, _first),
                CaptureOverlayWindow.SetImageAsync(TriptychFirstImage, _first));
            await RecomputeAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            _services.Log.Error("Compare", ex);
        }
    }

    private ImageDifferenceOptions CurrentOptions() => new(
        Threshold: (int)Math.Clamp(Math.Round(ThresholdSlider.Value), 0, 255),
        IgnoreAlpha: true,
        IgnoreFullyTransparent: IgnoreTransparentCheck.IsChecked == true);

    private async Task RecomputeAsync()
    {
        if (_closed) return;
        var generation = ++_renderGeneration;
        var options = CurrentOptions();
        var autoAlign = _autoAlignTranslation;
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _recomputeCts, cts);
        previous?.Cancel();
        StatusText.Text = autoAlign ? "Comparing and auto-aligning…" : "Comparing…";
        try
        {
            var result = await Task.Run(
                () => new ImageCompareService().Compare(_first, _second, options, autoAlign, _autoRegisterContent, cts.Token),
                cts.Token);
            if (_closed || generation != _renderGeneration || cts.IsCancellationRequested) return;
            _result = result;
            await Task.WhenAll(
                CaptureOverlayWindow.SetImageAsync(DifferenceImage, result.DifferencePng),
                CaptureOverlayWindow.SetImageAsync(HeatmapImage, result.HeatmapPng),
                CaptureOverlayWindow.SetImageAsync(MaskImage, result.MaskPng),
                CaptureOverlayWindow.SetImageAsync(OverlaySecondImage, result.AlignedSecondPng),
                CaptureOverlayWindow.SetImageAsync(TriptychSecondImage, result.AlignedSecondPng),
                CaptureOverlayWindow.SetImageAsync(TriptychDifferenceImage, result.DifferencePng));
            if (_closed || generation != _renderGeneration || cts.IsCancellationRequested) return;

            var psnr = double.IsPositiveInfinity(result.PeakSignalToNoiseRatio) ? "∞" : $"{result.PeakSignalToNoiseRatio:F2} dB";
            var compared = result.ComparedPixelCount == result.TotalPixelCount
                ? $"{result.TotalPixelCount:N0} px"
                : $"{result.ComparedPixelCount:N0}/{result.TotalPixelCount:N0} px";
            StatusText.Text = $"Canvas {result.CanvasWidth}×{result.CanvasHeight} • changed {result.ChangedPixelPercent:F2}% ({result.ChangedPixelCount:N0}/{compared}) • mean ΔRGB {result.MeanAbsoluteDifference:F2}/255 • ΔB/G/R {result.MeanBlueDifference:F1}/{result.MeanGreenDifference:F1}/{result.MeanRedDifference:F1} • SSIM {result.StructuralSimilarity:F4} • PSNR {psnr}";
            AlignmentText.Text = (_autoAlignTranslation || _autoRegisterContent)
                ? $"Registration: {(_autoRegisterContent ? "content" : "off")} · shift {result.AlignmentOffsetX:+#;-#;0},{result.AlignmentOffsetY:+#;-#;0} px · error {result.AlignmentError:F2}"
                : "Alignment: off";
            PerceptualText.Text = $"dHash distance: {result.PerceptualHashDistance}/64";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A newer compare request or window close superseded this calculation.
        }
        catch (Exception ex)
        {
            if (_closed || generation != _renderGeneration) return;
            StatusText.Text = ex.Message;
            _services.Log.Error("CompareRecompute", ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _recomputeCts, null, cts);
            cts.Dispose();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _blinkTimer.Stop();
        _compareDebounceTimer.Stop();
        _blinkTimer.Tick -= BlinkTimer_Tick;
        _compareDebounceTimer.Tick -= CompareDebounceTimer_Tick;
        Activated -= OnActivated;
        Interlocked.Exchange(ref _recomputeCts, null)?.Cancel();
        Interlocked.Exchange(ref _semanticCts, null)?.Cancel();
    }

    private void QueueRecompute()
    {
        if (!_initialized) return;
        _compareDebounceTimer.Stop();
        _compareDebounceTimer.Start();
    }

    private async void CompareDebounceTimer_Tick(object? sender, object e)
    {
        _compareDebounceTimer.Stop();
        await RecomputeAsync();
    }

    private void ThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ThresholdText is null) return;
        ThresholdText.Text = Math.Round(e.NewValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        QueueRecompute();
    }

    private void CompareOption_Changed(object sender, RoutedEventArgs e) => QueueRecompute();

    private async void AutoAlign_Click(object sender, RoutedEventArgs e)
    {
        _autoAlignTranslation = !_autoAlignTranslation;
        AlignmentText.Text = _autoAlignTranslation ? "Alignment: computing…" : "Alignment: off";
        await RecomputeAsync();
    }

    private async void AutoRegister_Click(object sender, RoutedEventArgs e)
    {
        _autoRegisterContent = !_autoRegisterContent;
        AlignmentText.Text = _autoRegisterContent ? "Registration: content bounds…" : (_autoAlignTranslation ? "Alignment: computing…" : "Alignment: off");
        await RecomputeAsync();
    }

    private async void SemanticDiff_Click(object sender, RoutedEventArgs e)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _semanticCts, cts);
        previous?.Cancel();
        try
        {
            SemanticSummaryText.Text = "Running local OCR semantic diff…";
            var includeTable = _services.Entitlements.CanUse(ProductFeature.TableExtraction);
            var result = await new CompareSemanticAnalysisService(_services.Ocr).AnalyzeAsync(
                _first, _second, _services.Settings.PreferredOcrLanguage, includeTable, cts.Token);
            if (_closed || cts.IsCancellationRequested) return;
            _semanticResult = result;
            RenderSemanticHighlights(result);
            var tableSummary = result.TableDiff is null
                ? "table n/a"
                : $"table {result.TableDiff.Changes.Count}{(result.TableDiff.IsTruncated ? "+" : string.Empty)} cell change(s)";
            SemanticSummaryText.Text = $"OCR: +{result.WordDiff.AddedCount} / -{result.WordDiff.RemovedCount} word(s) · layout {result.LayoutDiff.Changes.Count}{(result.LayoutDiff.IsTruncated ? "+" : string.Empty)} change(s) · {tableSummary}. Changed words are highlighted on image B.";
            SemanticDetailsBox.Text = BuildSemanticDetails(result);
            SemanticDetailsBox.Visibility = string.IsNullOrWhiteSpace(SemanticDetailsBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed) SemanticSummaryText.Text = ex.Message;
            _services.Log.Error("CompareSemantic", ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _semanticCts, null, cts);
            cts.Dispose();
        }
    }

    private static string BuildSemanticDetails(CompareSemanticAnalysisResult result)
    {
        var lines = new List<string>(64);
        foreach (var change in result.WordDiff.Changes.Where(change => change.Kind != OcrWordChangeKind.Equal).Take(32))
            lines.Add($"WORD {change.Kind}: {change.Text}");
        foreach (var change in result.LayoutDiff.Changes.Take(16))
            lines.Add($"LAYOUT: {(change.TextChanged ? "text " : string.Empty)}{(change.Moved ? "moved " : string.Empty)}{change.Text}".TrimEnd());
        if (result.TableDiff is not null)
            foreach (var change in result.TableDiff.Changes.Take(24))
                lines.Add($"CELL R{change.Row + 1}C{change.Column + 1}: {change.Left} → {change.Right}");
        return string.Join(Environment.NewLine, lines);
    }

    private void RenderSemanticHighlights(CompareSemanticAnalysisResult result)
    {
        SemanticHighlightCanvas.Children.Clear();
        SemanticHighlightCanvas.Width = result.RightWidth;
        SemanticHighlightCanvas.Height = result.RightHeight;
        var added = result.WordDiff.Changes.Where(change => change.Kind == OcrWordChangeKind.Added).Take(256);
        foreach (var change in added)
        {
            var bounds = change.Bounds.Intersect(new Magic.Capture.Core.Geometry.PixelRect(0, 0, result.RightWidth, result.RightHeight));
            if (bounds.IsEmpty) continue;
            var rectangle = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(36, 255, 69, 0))
            };
            Canvas.SetLeft(rectangle, bounds.X);
            Canvas.SetTop(rectangle, bounds.Y);
            SemanticHighlightCanvas.Children.Add(rectangle);
        }
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "Magic Capture Desktop_Compare_Report" };
            picker.FileTypeChoices.Add("HTML report", [".html"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            var html = BuildHtmlReport();
            await File.WriteAllTextAsync(file.Path, html);
            StatusText.Text = $"Report saved: {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            _services.Log.Error("CompareReport", ex);
        }
    }

    private string BuildHtmlReport()
    {
        var result = _result ?? throw new InvalidOperationException("Run comparison first.");
        static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        var semantic = _semanticResult is null
            ? "<p>Semantic diff not run.</p>"
            : $"<p>OCR +{_semanticResult.WordDiff.AddedCount} / -{_semanticResult.WordDiff.RemovedCount}; layout {_semanticResult.LayoutDiff.Changes.Count}; table {_semanticResult.TableDiff?.Changes.Count ?? 0}.</p>";
        return $"<!doctype html><meta charset='utf-8'><title>Magic Capture Desktop Compare Report</title><style>body{{font:14px system-ui;max-width:960px;margin:32px auto;padding:0 18px}}table{{border-collapse:collapse}}td,th{{border:1px solid #bbb;padding:6px 10px}}</style><h1>Magic Capture Desktop Compare Report</h1><p><b>A:</b> {H(FirstNameText.Text)}<br><b>B:</b> {H(SecondNameText.Text)}</p><table><tr><th>Metric</th><th>Value</th></tr><tr><td>Changed pixels</td><td>{result.ChangedPixelPercent:F3}%</td></tr><tr><td>Mean ΔRGB</td><td>{result.MeanAbsoluteDifference:F3}</td></tr><tr><td>SSIM</td><td>{result.StructuralSimilarity:F6}</td></tr><tr><td>PSNR</td><td>{result.PeakSignalToNoiseRatio:F3}</td></tr><tr><td>dHash distance</td><td>{result.PerceptualHashDistance}/64</td></tr><tr><td>Registration</td><td>{H(AlignmentText.Text)}</td></tr></table>{semantic}<p>Generated locally by Magic Capture Desktop.</p>";
    }

    private void SideBySide_Click(object sender, RoutedEventArgs e) => ShowMode(SideBySidePanel);
    private void Overlay_Click(object sender, RoutedEventArgs e) { StopBlink(); ShowMode(OverlayPanel); }
    private void Difference_Click(object sender, RoutedEventArgs e) => ShowMode(DifferencePanel);
    private void Heatmap_Click(object sender, RoutedEventArgs e) => ShowMode(HeatmapPanel);
    private void Mask_Click(object sender, RoutedEventArgs e) => ShowMode(MaskPanel);
    private void Triptych_Click(object sender, RoutedEventArgs e) => ShowMode(TriptychPanel);

    private void Blink_Click(object sender, RoutedEventArgs e)
    {
        ShowMode(OverlayPanel);
        _blinkShowingSecond = true;
        OverlaySecondImage.Opacity = 1;
        _blinkTimer.Start();
        StatusText.Text = _result is null ? "Blink compare." : $"Blink compare • threshold {CurrentOptions().Threshold}.";
    }

    private void BlinkTimer_Tick(object? sender, object e)
    {
        _blinkShowingSecond = !_blinkShowingSecond;
        OverlaySecondImage.Opacity = _blinkShowingSecond ? 1 : 0;
    }

    private void StopBlink()
    {
        if (!_blinkTimer.IsEnabled) return;
        _blinkTimer.Stop();
        OverlaySecondImage.Opacity = OverlayOpacitySlider.Value / 100d;
    }

    private void ShowMode(FrameworkElement visible)
    {
        if (visible != OverlayPanel) StopBlink();
        SideBySidePanel.Visibility = visible == SideBySidePanel ? Visibility.Visible : Visibility.Collapsed;
        OverlayPanel.Visibility = visible == OverlayPanel ? Visibility.Visible : Visibility.Collapsed;
        DifferencePanel.Visibility = visible == DifferencePanel ? Visibility.Visible : Visibility.Collapsed;
        HeatmapPanel.Visibility = visible == HeatmapPanel ? Visibility.Visible : Visibility.Collapsed;
        MaskPanel.Visibility = visible == MaskPanel ? Visibility.Visible : Visibility.Collapsed;
        TriptychPanel.Visibility = visible == TriptychPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OverlayOpacity_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (OverlaySecondImage is null || OverlayOpacityText is null) return;
        if (!_blinkTimer.IsEnabled) OverlaySecondImage.Opacity = e.NewValue / 100d;
        OverlayOpacityText.Text = $"{e.NewValue:F0}%";
    }

    private async void CopyDifference_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        try
        {
            await _services.Clipboard.CopyImageAsync(_result.DifferencePng);
            StatusText.Text = "Difference image copied.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            _services.Log.Error("CompareCopy", ex);
        }
    }

    private async void SaveDifference_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        try
        {
            var asset = CaptureAsset.Create(
                new Magic.Capture.Core.Geometry.PixelRect(0, 0, _result.CanvasWidth, _result.CanvasHeight),
                _result.DifferencePng,
                CaptureSourceKind.Compare,
                "Compare Difference");
            var file = await _services.Export.SaveImageAsAsync(this, asset, "png", _services.Settings.JpegQuality, "Magic Capture Desktop_Compare_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}");
            StatusText.Text = file is null ? "Save cancelled." : $"Saved {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            _services.Log.Error("CompareSave", ex);
        }
    }
}
