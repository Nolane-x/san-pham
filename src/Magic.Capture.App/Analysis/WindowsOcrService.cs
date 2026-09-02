using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using CoreOcrLine = Magic.Capture.Core.Ocr.OcrLine;
using CoreOcrWord = Magic.Capture.Core.Ocr.OcrWord;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Analysis;

internal sealed class WindowsOcrService : ITextRecognitionService
{
    public IReadOnlyList<string> AvailableLanguageTags =>
        OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();

    public async Task<OcrDocument> RecognizeAsync(byte[] pngBytes, string? languageTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        randomAccessStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        var resize = OcrResizePlan.Create(
            checked((int)decoder.PixelWidth),
            checked((int)decoder.PixelHeight),
            checked((int)OcrEngine.MaxImageDimension));

        using var softwareBitmap = await DecodeForOcrAsync(decoder, resize);
        var engine = CreateEngine(languageTag)
            ?? throw new InvalidOperationException("No compatible Windows OCR language is installed.");

        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var lines = new List<CoreOcrLine>();
        foreach (var nativeLine in result.Lines)
        {
            var words = nativeLine.Words.Select(nativeWord =>
            {
                var rect = nativeWord.BoundingRect;
                return new CoreOcrWord(nativeWord.Text, ScaleToOriginal(rect, resize));
            }).ToArray();

            var bounds = words.Length == 0
                ? PixelRect.Empty
                : words.Select(word => word.Bounds).Aggregate(PixelRect.Empty, PixelRect.Union);
            lines.Add(new CoreOcrLine(nativeLine.Text, bounds, words));
        }

        return new OcrDocument(result.Text ?? string.Empty, lines, result.TextAngle);
    }

    private static async Task<SoftwareBitmap> DecodeForOcrAsync(BitmapDecoder decoder, OcrResizePlan resize)
    {
        if (!resize.RequiresResize)
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)resize.TargetWidth,
            ScaledHeight = (uint)resize.TargetHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
    }

    private static PixelRect ScaleToOriginal(Windows.Foundation.Rect rect, OcrResizePlan resize) =>
        new(
            (int)Math.Round(rect.X * resize.ScaleXToOriginal),
            (int)Math.Round(rect.Y * resize.ScaleYToOriginal),
            Math.Max(1, (int)Math.Round(rect.Width * resize.ScaleXToOriginal)),
            Math.Max(1, (int)Math.Round(rect.Height * resize.ScaleYToOriginal)));

    private static OcrEngine? CreateEngine(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            var installed = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(language =>
                string.Equals(language.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase));
            if (installed is not null) return OcrEngine.TryCreateFromLanguage(installed);

            try
            {
                var requested = new Language(languageTag);
                var engine = OcrEngine.TryCreateFromLanguage(requested);
                if (engine is not null) return engine;
            }
            catch (Exception)
            {
                // Invalid/unavailable language falls back to the user's profile languages.
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }
}
