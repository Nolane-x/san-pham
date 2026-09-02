using System.Text;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Ocr;

public enum OcrSpatialMatchKind { Word, Line, Block }

public sealed record OcrSpatialMatch(
    OcrSpatialMatchKind Kind,
    string Text,
    PixelRect Bounds,
    int LineIndex,
    int? WordIndex = null);

public sealed record OcrSearchResult(IReadOnlyList<OcrSpatialMatch> Matches, bool IsTruncated);

/// <summary>Bounded immutable OCR geometry index for pointer hit-testing and screenshot search.</summary>
public sealed class OcrSpatialIndex
{
    public const int MaximumWords = 8_192;
    public const int MaximumLines = 2_048;
    public const int MaximumSearchMatches = 256;
    public const int MaximumBlocks = 512;
    public const int MaximumQueryCharacters = 256;
    public const int MaximumBlockCharacters = 16_384;
    private const int CellSize = 96;
    private const int MaximumBucketsPerWord = 16;

    private readonly IReadOnlyList<OcrSpatialMatch> _words;
    private readonly IReadOnlyList<OcrSpatialMatch> _lines;
    private readonly IReadOnlyList<OcrSpatialMatch> _blocks;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<int>> _wordBuckets;

    private OcrSpatialIndex(
        IReadOnlyList<OcrSpatialMatch> words,
        IReadOnlyList<OcrSpatialMatch> lines,
        IReadOnlyList<OcrSpatialMatch> blocks,
        IReadOnlyDictionary<long, IReadOnlyList<int>> wordBuckets)
    {
        _words = words;
        _lines = lines;
        _blocks = blocks;
        _wordBuckets = wordBuckets;
    }

    public int WordCount => _words.Count;
    public int LineCount => _lines.Count;
    public int BlockCount => _blocks.Count;

    public static OcrSpatialIndex Create(OcrDocument? document)
    {
        if (document is null) return new OcrSpatialIndex([], [], [], new Dictionary<long, IReadOnlyList<int>>());

        var words = new List<OcrSpatialMatch>(Math.Min(MaximumWords, 512));
        var lines = new List<OcrSpatialMatch>(Math.Min(MaximumLines, document.Lines.Count));
        var mutableBuckets = new Dictionary<long, List<int>>();
        var lineCount = Math.Min(document.Lines.Count, MaximumLines);
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var line = document.Lines[lineIndex];
            if (!line.Bounds.IsEmpty && !string.IsNullOrWhiteSpace(line.Text))
                lines.Add(new OcrSpatialMatch(OcrSpatialMatchKind.Line, NormalizeText(line.Text), line.Bounds, lineIndex));

            for (var wordIndex = 0; wordIndex < line.Words.Count && words.Count < MaximumWords; wordIndex++)
            {
                var word = line.Words[wordIndex];
                if (word.Bounds.IsEmpty || string.IsNullOrWhiteSpace(word.Text)) continue;
                var itemIndex = words.Count;
                words.Add(new OcrSpatialMatch(OcrSpatialMatchKind.Word, NormalizeText(word.Text), word.Bounds, lineIndex, wordIndex));
                var bucketCount = 0;
                foreach (var key in Cells(word.Bounds))
                {
                    if (bucketCount++ >= MaximumBucketsPerWord) break;
                    if (!mutableBuckets.TryGetValue(key, out var bucket)) mutableBuckets[key] = bucket = [];
                    bucket.Add(itemIndex);
                }
            }

            if (words.Count >= MaximumWords) break;
        }

        var blocks = BuildBlocks(lines);
        return new OcrSpatialIndex(
            words,
            lines,
            blocks,
            mutableBuckets.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value.ToArray()));
    }

    public OcrSpatialMatch? FindWord(PixelPoint point)
    {
        if (!_wordBuckets.TryGetValue(CellKey(FloorDiv(point.X, CellSize), FloorDiv(point.Y, CellSize)), out var candidates)) return null;
        OcrSpatialMatch? best = null;
        foreach (var index in candidates)
        {
            if ((uint)index >= (uint)_words.Count) continue;
            var candidate = _words[index];
            if (!candidate.Bounds.Contains(point)) continue;
            if (best is null || candidate.Bounds.Area < best.Bounds.Area) best = candidate;
        }
        return best;
    }

    public OcrSpatialMatch? FindLine(PixelPoint point)
    {
        OcrSpatialMatch? best = null;
        foreach (var line in _lines)
        {
            if (!line.Bounds.Contains(point)) continue;
            if (best is null || line.Bounds.Area < best.Bounds.Area) best = line;
        }
        return best;
    }

    public OcrSpatialMatch? FindBlock(PixelPoint point)
    {
        OcrSpatialMatch? best = null;
        foreach (var block in _blocks)
        {
            if (!block.Bounds.Contains(point)) continue;
            if (best is null || block.Bounds.Area < best.Bounds.Area) best = block;
        }
        return best;
    }

    public IReadOnlyList<OcrSpatialMatch> Search(string? query) => SearchDetailed(query).Matches;

    public OcrSearchResult SearchDetailed(string? query)
    {
        query = NormalizeQuery(query);
        if (query.Length == 0) return new OcrSearchResult([], false);
        var phrase = query.Any(char.IsWhiteSpace);
        var source = phrase ? _lines : _words;
        var matches = new List<OcrSpatialMatch>(Math.Min(MaximumSearchMatches, source.Count));
        var truncated = false;
        foreach (var candidate in source)
        {
            if (!candidate.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (matches.Count == MaximumSearchMatches)
            {
                truncated = true;
                break;
            }
            matches.Add(candidate);
        }
        return new OcrSearchResult(matches, truncated);
    }

    private sealed class BlockBuilder
    {
        public List<OcrSpatialMatch> Lines { get; } = [];
        public PixelRect Bounds { get; private set; } = PixelRect.Empty;

        public void Add(OcrSpatialMatch line)
        {
            Lines.Add(line);
            Bounds = PixelRect.Union(Bounds, line.Bounds);
        }
    }

    private static IReadOnlyList<OcrSpatialMatch> BuildBlocks(IReadOnlyList<OcrSpatialMatch> lines)
    {
        if (lines.Count == 0) return [];
        var heights = lines.Select(line => Math.Max(1, line.Bounds.Height)).Order().ToArray();
        var medianHeight = heights[heights.Length / 2];
        var verticalThreshold = Math.Max(12, (int)Math.Round(medianHeight * 1.6));
        var horizontalThreshold = Math.Max(18, medianHeight * 2);
        var builders = new List<BlockBuilder>(Math.Min(MaximumBlocks, lines.Count));

        foreach (var line in lines.OrderBy(item => item.Bounds.Y).ThenBy(item => item.Bounds.X))
        {
            BlockBuilder? best = null;
            var bestScore = double.MaxValue;
            foreach (var candidate in builders)
            {
                var verticalGap = Math.Max(0, line.Bounds.Y - candidate.Bounds.Bottom);
                if (verticalGap > verticalThreshold) continue;
                var overlap = HorizontalOverlapRatio(candidate.Bounds, line.Bounds);
                var horizontalGap = HorizontalGap(candidate.Bounds, line.Bounds);
                if (overlap < 0.15 && horizontalGap > horizontalThreshold) continue;
                var score = verticalGap * 4d + horizontalGap - overlap * 100d;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best is null)
            {
                if (builders.Count >= MaximumBlocks) continue;
                best = new BlockBuilder();
                builders.Add(best);
            }
            best.Add(line);
        }

        return builders
            .Where(builder => builder.Lines.Count > 0)
            .OrderBy(builder => builder.Bounds.Y)
            .ThenBy(builder => builder.Bounds.X)
            .Select(builder => new OcrSpatialMatch(
                OcrSpatialMatchKind.Block,
                BuildBlockText(builder.Lines),
                builder.Bounds,
                builder.Lines.Min(line => line.LineIndex)))
            .ToArray();
    }

    private static string BuildBlockText(IReadOnlyList<OcrSpatialMatch> lines)
    {
        var builder = new StringBuilder(Math.Min(MaximumBlockCharacters, lines.Sum(line => Math.Min(line.Text.Length, 512))));
        foreach (var line in lines.OrderBy(item => item.Bounds.Y).ThenBy(item => item.Bounds.X))
        {
            if (builder.Length > 0 && builder.Length + 2 <= MaximumBlockCharacters) builder.Append("\r\n");
            var remaining = MaximumBlockCharacters - builder.Length;
            if (remaining <= 0) break;
            var count = Math.Min(remaining, line.Text.Length);
            builder.Append(line.Text, 0, count);
        }
        return builder.ToString();
    }

    private static double HorizontalOverlapRatio(PixelRect a, PixelRect b)
    {
        var overlap = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X));
        var minWidth = Math.Min(a.Width, b.Width);
        return minWidth <= 0 ? 0 : overlap / (double)minWidth;
    }

    private static int HorizontalGap(PixelRect a, PixelRect b)
    {
        if (a.Right < b.X) return b.X - a.Right;
        if (b.Right < a.X) return a.X - b.Right;
        return 0;
    }

    private static string NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        var value = query.Trim();
        return value.Length <= MaximumQueryCharacters ? value : value[..MaximumQueryCharacters];
    }

    private static string NormalizeText(string text)
    {
        text = text.Trim();
        return text.Length <= 2_048 ? text : text[..2_048];
    }

    private static IEnumerable<long> Cells(PixelRect bounds)
    {
        var left = FloorDiv(bounds.X, CellSize);
        var top = FloorDiv(bounds.Y, CellSize);
        var right = FloorDiv(checked(bounds.Right - 1), CellSize);
        var bottom = FloorDiv(checked(bounds.Bottom - 1), CellSize);
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                yield return CellKey(x, y);
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;
}
