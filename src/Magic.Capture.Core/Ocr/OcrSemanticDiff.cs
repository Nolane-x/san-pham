using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Ocr;

public enum OcrWordChangeKind { Equal, Added, Removed }

public sealed record OcrWordChange(OcrWordChangeKind Kind, string Text, PixelRect Bounds, int Index);
public sealed record OcrSemanticDiffResult(IReadOnlyList<OcrWordChange> Changes, int AddedCount, int RemovedCount, bool IsTruncated);

public static class OcrSemanticDiff
{
    public const int MaximumWordsPerSide = 1_024;
    public const int MaximumChanges = 512;

    public static OcrSemanticDiffResult Compare(OcrDocument left, OcrDocument right, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        var leftWords = left.Lines.SelectMany(line => line.Words).Take(MaximumWordsPerSide + 1).ToArray();
        var rightWords = right.Lines.SelectMany(line => line.Words).Take(MaximumWordsPerSide + 1).ToArray();
        var leftTruncated = leftWords.Length > MaximumWordsPerSide;
        var rightTruncated = rightWords.Length > MaximumWordsPerSide;
        var a = leftWords.Take(MaximumWordsPerSide).ToArray();
        var b = rightWords.Take(MaximumWordsPerSide).ToArray();
        var dp = new ushort[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            if ((i & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var j = b.Length - 1; j >= 0; j--)
                dp[i, j] = Same(a[i].Text, b[j].Text) ? (ushort)(dp[i + 1, j + 1] + 1) : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        }

        var changes = new List<OcrWordChange>(Math.Min(MaximumChanges, a.Length + b.Length));
        var added = 0; var removed = 0; var ai = 0; var bi = 0; var truncated = false;
        void Add(OcrWordChange item)
        {
            if (changes.Count >= MaximumChanges) { truncated = true; return; }
            changes.Add(item);
        }
        while (ai < a.Length || bi < b.Length)
        {
            if (truncated) break;
            if (ai < a.Length && bi < b.Length && Same(a[ai].Text, b[bi].Text)) { Add(new(OcrWordChangeKind.Equal, b[bi].Text, b[bi].Bounds, bi)); ai++; bi++; }
            else if (bi < b.Length && (ai == a.Length || dp[ai, bi + 1] >= dp[ai + 1, bi])) { Add(new(OcrWordChangeKind.Added, b[bi].Text, b[bi].Bounds, bi)); added++; bi++; }
            else { Add(new(OcrWordChangeKind.Removed, a[ai].Text, a[ai].Bounds, ai)); removed++; ai++; }
        }
        truncated |= leftTruncated || rightTruncated;
        return new(changes, added, removed, truncated);
    }

    private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
