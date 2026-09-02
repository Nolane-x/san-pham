using System.Globalization;
using Magic.Capture.App.Analysis;
using Magic.Capture.App.Capture;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.Signals;
using Magic.Capture.Core.Tables;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Magic.Capture.App.Views;

internal enum CaptureResultTab { Preview = 0, Text = 1, Table = 2, Barcode = 3, Signals = 4, Metadata = 5 }

public sealed partial class CaptureResultWindow : Window
{
    private const string AutoOcrLanguageLabel = "Auto — Windows profile";

    private readonly CaptureAsset _asset;
    private readonly ApplicationServices _services;
    private readonly bool _extendedRecognition;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CaptureAnalysis? _analysis;
    private OcrSpatialIndex? _ocrIndex;
    private TableSchemaInference? _tableSchema;
    private CancellationTokenSource? _ocrRerunCts;
    private int _analysisGeneration;
    private bool _initialized;

    internal CaptureResultWindow(CaptureAsset asset, ApplicationServices services, CaptureResultTab initialTab = CaptureResultTab.Preview)
    {
        InitializeComponent();
        _asset = asset;
        _services = services;
        _extendedRecognition = _services.Entitlements.CanUse(ProductFeature.TableExtraction);
        Platform.WindowHelpers.MoveAndResize(this, 160, 100, Math.Min(1100, Math.Max(760, asset.Width + 80)), 760);
        TableTab.Visibility = _extendedRecognition ? Visibility.Visible : Visibility.Collapsed;
        BarcodeTab.Visibility = _extendedRecognition ? Visibility.Visible : Visibility.Collapsed;
        BmpFormatItem.IsEnabled = _services.Entitlements.CanUse(ProductFeature.AdvancedImageExport);
        TiffFormatItem.IsEnabled = _services.Entitlements.CanUse(ProductFeature.AdvancedImageExport);
        ResultTabs.SelectedIndex = !_extendedRecognition && initialTab is CaptureResultTab.Table or CaptureResultTab.Barcode
            ? (int)CaptureResultTab.Text
            : (int)initialTab;

        PreviewSurface.Width = Math.Max(1, asset.Width);
        PreviewSurface.Height = Math.Max(1, asset.Height);
        PreviewImage.Width = Math.Max(1, asset.Width);
        PreviewImage.Height = Math.Max(1, asset.Height);
        OcrOverlayCanvas.Width = Math.Max(1, asset.Width);
        OcrOverlayCanvas.Height = Math.Max(1, asset.Height);
        OcrSelectionRectangle.Stroke = new SolidColorBrush(Colors.DeepSkyBlue);
        InitializeOcrLanguageChoices();
        RerunOcrButton.IsEnabled = false;

        Activated += OnActivated;
        Closed += OnClosed;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized || args.WindowActivationState == WindowActivationState.Deactivated) return;
        _initialized = true;
        try
        {
            var token = _lifetimeCts.Token;
            await CaptureOverlayWindow.SetImageAsync(PreviewImage, _asset.PngBytes);
            token.ThrowIfCancellationRequested();
            MetadataTextBox.Text = $"Id: {_asset.Id}\r\nCreated (UTC): {_asset.CreatedUtc:O}\r\nSource: {_asset.SourceKind}\r\nBounds: {_asset.PixelBounds.X},{_asset.PixelBounds.Y} {_asset.Width}×{_asset.Height}\r\nBytes: {_asset.PngBytes.LongLength:N0}";
            FooterText.Text = "Recognition runs locally using Windows OCR and deterministic decoders.";

            CaptureAnalysis analysis;
            if (_extendedRecognition)
            {
                analysis = await _services.Analysis.AnalyzeAsync(_asset, _services.Settings, token);
            }
            else
            {
                var ocr = await _services.Ocr.RecognizeAsync(_asset.PngBytes, _services.Settings.PreferredOcrLanguage, token);
                analysis = new CaptureAnalysis(ocr, null, []);
            }

            token.ThrowIfCancellationRequested();
            ApplyAnalysis(analysis);
            await TryUpdateHistoryPreviewsAsync(analysis, token);
            AnalysisStatusText.Text = _extendedRecognition ? "Local Plus analysis ready" : "Local OCR ready";
            RerunOcrButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Window is closing; no UI update is necessary.
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            AnalysisStatusText.Text = "Analysis unavailable";
            FooterText.Text = ex.Message;
            RerunOcrButton.IsEnabled = true;
            _services.Log.Error("ResultAnalysis", ex);
        }
    }

    private void ApplyAnalysis(CaptureAnalysis analysis)
    {
        _analysis = analysis;
        _ocrIndex = OcrSpatialIndex.Create(analysis.Ocr);
        _tableSchema = analysis.Table is null ? null : TableCellInference.Infer(analysis.Table, CultureInfo.CurrentCulture);
        UpdateOcrTextOutput();
        UpdateOcrSearchHighlights();
        BarcodeList.ItemsSource = analysis.Barcodes;
        SignalsList.ItemsSource = analysis.Signals;
        UpdateTableOutput();
    }

    private async Task TryUpdateHistoryPreviewsAsync(CaptureAnalysis analysis, CancellationToken cancellationToken)
    {
        try
        {
            await _services.HistoryStore.UpdatePreviewsAsync(
                _asset.Id,
                analysis.Ocr.Text,
                _extendedRecognition ? string.Join(" | ", analysis.Barcodes.Select(hit => hit.Text)) : null,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _services.Log.Error("ResultHistoryPreview", ex);
            FooterText.Text = "OCR is ready, but the History preview could not be updated.";
        }
    }

    private void InitializeOcrLanguageChoices()
    {
        var languages = _services.Ocr.AvailableLanguageTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .Prepend(AutoOcrLanguageLabel)
            .ToArray();
        OcrResultLanguageCombo.ItemsSource = languages;
        var preferred = _services.Settings.PreferredOcrLanguage;
        var preferredIndex = string.IsNullOrWhiteSpace(preferred)
            ? 0
            : Array.FindIndex(languages, item => string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase));
        OcrResultLanguageCombo.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
    }

    private async void RerunOcr_Click(object sender, RoutedEventArgs e)
    {
        _ocrRerunCts?.Cancel();
        _ocrRerunCts?.Dispose();
        _ocrRerunCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _ocrRerunCts.Token;
        var generation = ++_analysisGeneration;
        RerunOcrButton.IsEnabled = false;
        OcrRerunProgress.IsActive = true;
        OcrRerunProgress.Visibility = Visibility.Visible;
        AnalysisStatusText.Text = "Re-running local OCR…";

        try
        {
            var selected = OcrResultLanguageCombo.SelectedItem as string;
            var languageTag = string.IsNullOrWhiteSpace(selected) || string.Equals(selected, AutoOcrLanguageLabel, StringComparison.Ordinal)
                ? null
                : selected;
            var ocr = await _services.Ocr.RecognizeAsync(_asset.PngBytes, languageTag, token);
            token.ThrowIfCancellationRequested();
            var table = _extendedRecognition ? TableExtractor.TryExtract(ocr) : null;
            var barcodes = _analysis?.Barcodes ?? [];
            var analysis = new CaptureAnalysis(ocr, table, barcodes);
            if (generation != _analysisGeneration || token.IsCancellationRequested) return;

            ApplyAnalysis(analysis);
            await TryUpdateHistoryPreviewsAsync(analysis, token);
            AnalysisStatusText.Text = languageTag is null ? "Local OCR ready · Windows profile" : $"Local OCR ready · {languageTag}";
            FooterText.Text = $"OCR re-ran locally with {analysis.Ocr.Lines.Count:N0} lines and {_ocrIndex?.WordCount ?? 0:N0} indexed words.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded rerun or closing window.
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            AnalysisStatusText.Text = "OCR rerun unavailable";
            FooterText.Text = ex.Message;
            _services.Log.Error("ResultOcrRerun", ex);
        }
        finally
        {
            if (generation == _analysisGeneration && !_lifetimeCts.IsCancellationRequested)
            {
                RerunOcrButton.IsEnabled = true;
                OcrRerunProgress.IsActive = false;
                OcrRerunProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void OpenLanguageSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var opened = await Launcher.LaunchUriAsync(new Uri("ms-settings:regionlanguage"));
            FooterText.Text = opened ? "Opened Windows language settings." : "Windows language settings could not be opened.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            FooterText.Text = ex.Message;
        }
    }

    private void OcrTextMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOcrTextOutput();

    private void UpdateOcrTextOutput()
    {
        if (_analysis is null) return;
        var tag = (OcrTextModeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var mode = tag switch
        {
            "layout" => OcrTextReconstructionMode.Layout,
            "code" => OcrTextReconstructionMode.Code,
            _ => OcrTextReconstructionMode.Plain
        };
        OcrTextBox.Text = OcrTextReconstruction.Build(_analysis.Ocr, mode);
    }

    private void OcrSearchBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateOcrSearchHighlights();

    private void UpdateOcrSearchHighlights()
    {
        while (OcrOverlayCanvas.Children.Count > 1)
            OcrOverlayCanvas.Children.RemoveAt(OcrOverlayCanvas.Children.Count - 1);

        var search = _ocrIndex?.SearchDetailed(OcrSearchBox.Text) ?? new OcrSearchResult([], false);
        var matches = search.Matches;
        OcrSearchCountText.Text = string.IsNullOrWhiteSpace(OcrSearchBox.Text)
            ? string.Empty
            : search.IsTruncated
                ? $"{matches.Count}+ matches"
                : $"{matches.Count} match{(matches.Count == 1 ? string.Empty : "es")}";

        foreach (var match in matches)
        {
            var bounds = ClampOcrBounds(match.Bounds);
            if (bounds.IsEmpty) continue;
            var rectangle = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = new SolidColorBrush(Colors.Gold),
                StrokeThickness = 2,
                Opacity = 0.9
            };
            Canvas.SetLeft(rectangle, bounds.X);
            Canvas.SetTop(rectangle, bounds.Y);
            OcrOverlayCanvas.Children.Add(rectangle);
        }
    }

    private void PreviewSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_ocrIndex is null) return;
        var position = e.GetCurrentPoint(PreviewSurface).Position;
        if (position.X < 0 || position.Y < 0 || position.X >= _asset.Width || position.Y >= _asset.Height) return;
        var point = new PixelPoint((int)Math.Floor(position.X), (int)Math.Floor(position.Y));
        var hitMode = (OcrHitModeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var match = hitMode switch
        {
            "block" => _ocrIndex.FindBlock(point),
            "line" => _ocrIndex.FindLine(point),
            _ => _ocrIndex.FindWord(point)
        };
        if (match is null)
        {
            OcrSelectionRectangle.Visibility = Visibility.Collapsed;
            PreviewOcrHintText.Text = $"No OCR {hitMode ?? "word"} at this point.";
            return;
        }

        ShowOcrSelection(match.Bounds);
        _services.Clipboard.CopyText(match.Text);
        PreviewOcrHintText.Text = $"Copied {match.Kind.ToString().ToLowerInvariant()}: {BoundStatusText(match.Text)}";
        FooterText.Text = "OCR selection copied.";
        e.Handled = true;
    }

    private void ShowOcrSelection(PixelRect bounds)
    {
        bounds = ClampOcrBounds(bounds);
        if (bounds.IsEmpty)
        {
            OcrSelectionRectangle.Visibility = Visibility.Collapsed;
            return;
        }
        OcrSelectionRectangle.Width = bounds.Width;
        OcrSelectionRectangle.Height = bounds.Height;
        Canvas.SetLeft(OcrSelectionRectangle, bounds.X);
        Canvas.SetTop(OcrSelectionRectangle, bounds.Y);
        OcrSelectionRectangle.Visibility = Visibility.Visible;
    }

    private PixelRect ClampOcrBounds(PixelRect bounds) =>
        bounds.Intersect(new PixelRect(0, 0, _asset.Width, _asset.Height));

    private static string BoundStatusText(string text)
    {
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return text.Length <= 120 ? text : text[..117] + "…";
    }

    private async void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        try { await _services.Clipboard.CopyImageAsync(_asset.PngBytes); FooterText.Text = "Image copied."; }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { FooterText.Text = ex.Message; }
    }

    private async void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var format = ((ImageFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string) ?? "png";
            if (format is "bmp" or "tif" or "tiff" && !_services.Entitlements.CanUse(ProductFeature.AdvancedImageExport))
            {
                FooterText.Text = "BMP and TIFF export are available during Plus trial and in Pro Lifetime.";
                ImageFormatCombo.SelectedIndex = 0;
                return;
            }
            var file = await _services.Export.SaveImageAsAsync(this, _asset, format, _services.Settings.JpegQuality, _services.Settings.FileNameTemplate);
            FooterText.Text = file is null ? "Save cancelled." : $"Saved {file.Name}";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { FooterText.Text = ex.Message; _services.Log.Error("ResultSaveImage", ex); }
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).OpenPin(_asset);
    private void Edit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).OpenAnnotation(_asset);
    private void Magic_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).OpenMagic(_asset);

    private void AddToContext_Click(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).AddToAiContext(_asset, _asset.SourceDisplayName ?? _asset.SourceKind.ToString()))
            FooterText.Text = $"Added to AI Context Stack ({_services.AiContext.Count}/8).";
    }

    private void CopySignal_Click(object sender, RoutedEventArgs e)
    {
        if (SignalsList.SelectedItem is not TextSignal signal) return;
        _services.Clipboard.CopyText(signal.Value);
        FooterText.Text = $"{signal.Kind} copied.";
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        _services.Clipboard.CopyText(OcrTextBox.Text ?? string.Empty);
        FooterText.Text = "Text copied.";
    }

    private async void SaveText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await _services.Export.SaveTextAsAsync(this, OcrTextBox.Text ?? string.Empty, "Text", ".txt");
            FooterText.Text = file is null ? "Save cancelled." : $"Saved {file.Name}";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            FooterText.Text = ex.Message;
        }
    }

    private void TableFormat_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTableOutput();

    private void UpdateTableOutput()
    {
        if (_analysis?.Table is not { } table)
        {
            TableConfidenceText.Text = _extendedRecognition ? "No stable table structure detected." : string.Empty;
            TableDiagnosticsText.Text = string.Empty;
            TableOutputTextBox.Text = string.Empty;
            return;
        }

        _tableSchema ??= TableCellInference.Infer(table, CultureInfo.CurrentCulture);
        TableConfidenceText.Text = $"Detected {table.RowCount}×{table.ColumnCount} • confidence {table.Confidence:P0}";
        TableDiagnosticsText.Text = BuildTableDiagnostics(_tableSchema);
        var format = ((TableFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string) ?? "csv";
        var locale = SelectedTableLocaleMode();
        var culture = CultureInfo.CurrentCulture;
        try
        {
            TableOutputTextBox.Text = format switch
            {
                "csv-semicolon" => TableSerializers.ToDelimited(table, new TableDelimitedOptions(';', locale), culture),
                "tsv" => TableSerializers.ToDelimited(table, new TableDelimitedOptions('\t', locale), culture),
                "excel-tsv" => TableSerializers.ToDelimited(table, new TableDelimitedOptions('\t', locale, true), culture),
                "md" => TableSerializers.ToMarkdown(table),
                "html" => TableSerializers.ToHtml(table),
                "json" => TableSerializers.ToJson(table),
                _ => TableSerializers.ToDelimited(table, new TableDelimitedOptions(',', locale), culture)
            };
            CopyTableButton.IsEnabled = true;
            SaveTableButton.IsEnabled = true;
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            TableOutputTextBox.Text = string.Empty;
            CopyTableButton.IsEnabled = false;
            SaveTableButton.IsEnabled = false;
            TableDiagnosticsText.Text = $"{BuildTableDiagnostics(_tableSchema)} · output unavailable: {ex.Message}";
        }
    }

    private TableNumberLocaleMode SelectedTableLocaleMode() =>
        ((TableLocaleCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "invariant" => TableNumberLocaleMode.Invariant,
            "current" => TableNumberLocaleMode.CurrentCulture,
            _ => TableNumberLocaleMode.Preserve
        };

    private static string BuildTableDiagnostics(TableSchemaInference schema)
    {
        var columns = string.Join(" · ", schema.Columns.Take(12).Select(column =>
            $"C{column.Index + 1}:{column.DominantKind} {column.Confidence:P0}"));
        var extra = schema.Columns.Count > 12 ? $" · +{schema.Columns.Count - 12} columns" : string.Empty;
        if (schema.Anomalies.Count == 0)
            return $"{(schema.HasHeader ? "Header detected" : "No header inferred")} · {columns}{extra} · no type anomalies";

        var samples = string.Join("; ", schema.Anomalies.Take(6).Select(anomaly =>
            $"R{anomaly.RowIndex + 1}C{anomaly.ColumnIndex + 1} {anomaly.ExpectedKind}→{anomaly.ActualKind} '{BoundStatusText(anomaly.Value)}'"));
        var more = schema.Anomalies.Count > 6 ? $"; +{schema.Anomalies.Count - 6} more" : string.Empty;
        return $"{(schema.HasHeader ? "Header detected" : "No header inferred")} · {columns}{extra} · {schema.Anomalies.Count} anomal{(schema.Anomalies.Count == 1 ? "y" : "ies")}: {samples}{more}";
    }

    private void CopyTable_Click(object sender, RoutedEventArgs e)
    {
        _services.Clipboard.CopyText(TableOutputTextBox.Text ?? string.Empty);
        FooterText.Text = "Table output copied.";
    }

    private void OpenTableWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_analysis?.Table is not { } table) throw new InvalidOperationException("No table is available to edit.");
            var window = new TableWorkspaceWindow(table, _services);
            ((App)Application.Current).TrackChildWindow(window);
            window.Activate();
            FooterText.Text = "Opened bounded Table Workspace.";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            FooterText.Text = ex.Message;
        }
    }

    private async void SaveTable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var format = ((TableFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string) ?? "csv";
            var extension = format switch
            {
                "md" => ".md",
                "html" => ".html",
                "json" => ".json",
                "tsv" or "excel-tsv" => ".tsv",
                _ => ".csv"
            };
            var label = format switch { "excel-tsv" => "Excel TSV", "csv-semicolon" => "CSV", _ => format.ToUpperInvariant() };
            var file = await _services.Export.SaveTextAsAsync(this, TableOutputTextBox.Text ?? string.Empty, label, extension);
            FooterText.Text = file is null ? "Save cancelled." : $"Saved {file.Name}";
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            FooterText.Text = ex.Message;
        }
    }

    private void CopyBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (BarcodeList.SelectedItem is not BarcodeHit hit) return;
        _services.Clipboard.CopyText(hit.Text);
        FooterText.Text = "Barcode value copied.";
    }

    private async void OpenBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (BarcodeList.SelectedItem is not BarcodeHit { IsUri: true } hit || !Uri.TryCreate(hit.Text, UriKind.Absolute, out var uri))
        {
            FooterText.Text = "Select an HTTP/HTTPS barcode value first.";
            return;
        }
        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            FooterText.Text = ex.Message;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _lifetimeCts.Cancel();
        _ocrRerunCts?.Cancel();
        _ocrRerunCts?.Dispose();
        _ocrRerunCts = null;
        _lifetimeCts.Dispose();
        Activated -= OnActivated;
        Closed -= OnClosed;
    }
}
