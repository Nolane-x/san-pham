using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Ocr;

public sealed record OcrLayoutChange(string Text, PixelRect LeftBounds, PixelRect RightBounds, bool TextChanged, bool Moved);
public sealed record OcrLayoutDiffResult(IReadOnlyList<OcrLayoutChange> Changes, bool IsTruncated);

public static class OcrLayoutDiff
{
    public const int MaximumLines = 512;
    public const int MaximumChanges = 256;

    public static OcrLayoutDiffResult Compare(OcrDocument left, OcrDocument right, int leftWidth, int leftHeight, int rightWidth, int rightHeight)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        var leftLines = left.Lines.Take(MaximumLines).ToArray();
        var rightLines = right.Lines.Take(MaximumLines).ToArray();
        var used = new bool[rightLines.Length];
        var changes = new List<OcrLayoutChange>();
        for (var i = 0; i < leftLines.Length && changes.Count < MaximumChanges; i++)
        {
            var a = leftLines[i];
            var best = -1;
            var bestScore = double.MaxValue;
            for (var j = 0; j < rightLines.Length; j++)
            {
                if (used[j]) continue;
                var textPenalty = string.Equals(a.Text?.Trim(), rightLines[j].Text?.Trim(), StringComparison.OrdinalIgnoreCase) ? 0d : .35d;
                var score = textPenalty + Distance(Normalize(a.Bounds, leftWidth, leftHeight), Normalize(rightLines[j].Bounds, rightWidth, rightHeight));
                if (score < bestScore) { bestScore = score; best = j; }
            }
            if (best < 0)
            {
                changes.Add(new(a.Text ?? string.Empty, a.Bounds, PixelRect.Empty, true, true));
                continue;
            }
            used[best] = true;
            var b = rightLines[best];
            var textChanged = !string.Equals(a.Text?.Trim(), b.Text?.Trim(), StringComparison.OrdinalIgnoreCase);
            var moved = Distance(Normalize(a.Bounds, leftWidth, leftHeight), Normalize(b.Bounds, rightWidth, rightHeight)) > .03;
            if (textChanged || moved) changes.Add(new(b.Text ?? string.Empty, a.Bounds, b.Bounds, textChanged, moved));
        }
        for (var j = 0; j < rightLines.Length && changes.Count < MaximumChanges; j++)
            if (!used[j]) changes.Add(new(rightLines[j].Text ?? string.Empty, PixelRect.Empty, rightLines[j].Bounds, true, true));
        var truncated = left.Lines.Count > MaximumLines || right.Lines.Count > MaximumLines || changes.Count == MaximumChanges;
        return new(changes, truncated);
    }

    private static (double X, double Y, double W, double H) Normalize(PixelRect r, int w, int h) =>
        (r.X / (double)Math.Max(1, w), r.Y / (double)Math.Max(1, h), r.Width / (double)Math.Max(1, w), r.Height / (double)Math.Max(1, h));
    private static double Distance((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
        Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Max(Math.Abs(a.W - b.W), Math.Abs(a.H - b.H)));
}
