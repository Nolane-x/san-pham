using Magic.Capture.App.Capture;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.App.Analysis;

internal sealed class AnalysisService
{
    private readonly ITextRecognitionService _ocr;
    private readonly BarcodeService _barcode;

    public AnalysisService(ITextRecognitionService ocr, BarcodeService barcode)
    {
        _ocr = ocr;
        _barcode = barcode;
    }

    public async Task<CaptureAnalysis> AnalyzeAsync(CaptureAsset asset, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var ocrTask = _ocr.RecognizeAsync(asset.PngBytes, settings.PreferredOcrLanguage, cancellationToken);
        var barcodeTask = Task.Run(() => _barcode.Decode(asset.PngBytes), cancellationToken);
        await Task.WhenAll(ocrTask, barcodeTask);
        var ocr = await ocrTask;
        var barcodes = await barcodeTask;
        var table = TableExtractor.TryExtract(ocr);
        return new CaptureAnalysis(ocr, table, barcodes);
    }
}
