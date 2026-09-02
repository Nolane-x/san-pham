using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Ocr;

public sealed record OcrWord(string Text, PixelRect Bounds);

public sealed record OcrLine(string Text, PixelRect Bounds, IReadOnlyList<OcrWord> Words);

public sealed record OcrDocument(string Text, IReadOnlyList<OcrLine> Lines, double? TextAngleRadians)
{
    public IReadOnlyList<OcrWord> Words => Lines.SelectMany(line => line.Words).ToArray();
}
