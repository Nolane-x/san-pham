using Magic.Capture.App.Analysis;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.App.Imaging;

internal sealed record CompareSemanticAnalysisResult(
    OcrDocument LeftOcr,
    OcrDocument RightOcr,
    OcrSemanticDiffResult WordDiff,
    OcrLayoutDiffResult LayoutDiff,
    TableDiffResult? TableDiff,
    int RightWidth,
    int RightHeight);

internal sealed class CompareSemanticAnalysisService
{
    private readonly ITextRecognitionService _ocr;

    public CompareSemanticAnalysisService(ITextRecognitionService ocr) => _ocr = ocr;

    public async Task<CompareSemanticAnalysisResult> AnalyzeAsync(
        byte[] leftImage,
        byte[] rightImage,
        string? languageTag,
        bool includeTableDiff,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leftImage);
        ArgumentNullException.ThrowIfNull(rightImage);
        var leftTask = _ocr.RecognizeAsync(leftImage, languageTag, cancellationToken);
        var rightTask = _ocr.RecognizeAsync(rightImage, languageTag, cancellationToken);
        await Task.WhenAll(leftTask, rightTask);
        var left = await leftTask;
        var right = await rightTask;
        cancellationToken.ThrowIfCancellationRequested();

        int leftWidth, leftHeight, rightWidth, rightHeight;
        using (var a = BitmapCodec.DecodeForCompare(leftImage)) { leftWidth = a.Width; leftHeight = a.Height; }
        using (var b = BitmapCodec.DecodeForCompare(rightImage)) { rightWidth = b.Width; rightHeight = b.Height; }

        var wordDiff = OcrSemanticDiff.Compare(left, right, cancellationToken);
        var layoutDiff = OcrLayoutDiff.Compare(left, right, leftWidth, leftHeight, rightWidth, rightHeight);
        TableDiffResult? tableDiff = null;
        if (includeTableDiff)
        {
            var leftTable = TableExtractor.TryExtract(left);
            var rightTable = TableExtractor.TryExtract(right);
            if (leftTable is not null || rightTable is not null)
            {
                var leftDoc = leftTable is null ? EditableTableDocument.Create([], 0) : EditableTableDocument.FromDetectedTable(leftTable);
                var rightDoc = rightTable is null ? EditableTableDocument.Create([], 0) : EditableTableDocument.FromDetectedTable(rightTable);
                tableDiff = TableDiffEngine.Compare(leftDoc, rightDoc);
            }
        }

        return new(left, right, wordDiff, layoutDiff, tableDiff, rightWidth, rightHeight);
    }
}
