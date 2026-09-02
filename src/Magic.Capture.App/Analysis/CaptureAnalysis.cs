using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Tables;
using Magic.Capture.Core.Signals;

namespace Magic.Capture.App.Analysis;

internal sealed record CaptureAnalysis(OcrDocument Ocr, DetectedTable? Table, IReadOnlyList<BarcodeHit> Barcodes)
{
    public static CaptureAnalysis Empty => new(new OcrDocument(string.Empty, [], null), null, []);
    public IReadOnlyList<TextSignal> Signals => TextSignalExtractor.Extract(Ocr);
}
