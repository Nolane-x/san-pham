using Magic.Capture.Core.Ocr;

namespace Magic.Capture.App.Analysis;

internal interface ITextRecognitionService
{
    IReadOnlyList<string> AvailableLanguageTags { get; }
    Task<OcrDocument> RecognizeAsync(byte[] pngBytes, string? languageTag, CancellationToken cancellationToken = default);
}
