using System.Text;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;

namespace Magic.Capture.Core.ScreenGraph;

public sealed record UiAutomationOcrEvidence(string Text, IReadOnlyList<string> WordIds);

/// <summary>
/// Correlates OCR words with semantic UI Automation controls using a bounded spatial grid.
/// The correlation is intentionally local/deterministic and never exposes OCR from password controls.
/// </summary>
public static class UiAutomationOcrCorrelation
{
    public const int MaximumIndexedWords = 4_096;
    public const int MaximumEvidenceWordsPerNode = 16;
    public const int MaximumEvidenceTextLength = 512;
    private const int MaximumWordTextLength = 256;
    private const int CellSize = 128;
    private const int MaximumCellsPerWord = 64;
    private const int MaximumLargeWords = 128;

    private static readonly HashSet<string> ContainerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Window", "Pane", "Group", "Document", "DataGrid", "Table", "List", "Tree",
        "Menu", "MenuBar", "ToolBar", "StatusBar", "Tab", "Header", "TitleBar"
    };

    public static IReadOnlyDictionary<string, UiAutomationOcrEvidence> Correlate(
        IReadOnlyList<ScreenUiAutomationNode>? controls,
        OcrDocument? ocr)
    {
        if (controls is null || controls.Count == 0 || ocr is null || ocr.Lines.Count == 0)
            return new Dictionary<string, UiAutomationOcrEvidence>(StringComparer.Ordinal);

        var words = BuildWordIndex(ocr);
        if (words.Items.Count == 0)
            return new Dictionary<string, UiAutomationOcrEvidence>(StringComparer.Ordinal);

        var parentByKey = controls
            .Where(control => !string.IsNullOrWhiteSpace(control.StableKey))
            .GroupBy(control => control.StableKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ParentStableKey, StringComparer.Ordinal);
        var depthCache = new Dictionary<string, int>(StringComparer.Ordinal);

        var orderedControls = controls
            .Where(IsEligibleControl)
            .Where(control => !string.IsNullOrWhiteSpace(control.StableKey))
            .OrderByDescending(control => ResolveDepth(control.StableKey, parentByKey, depthCache))
            .ThenBy(control => control.Bounds.Area)
            .ThenBy(control => control.StableKey, StringComparer.Ordinal)
            .ToArray();

        var claimedWords = new HashSet<int>();
        var result = new Dictionary<string, UiAutomationOcrEvidence>(StringComparer.Ordinal);
        foreach (var control in orderedControls)
        {
            var matched = QueryCandidates(control.Bounds, words)
                .Where(index => !claimedWords.Contains(index))
                .Where(index => Matches(control.Bounds, words.Items[index].Bounds))
                .OrderBy(index => words.Items[index].Bounds.Y)
                .ThenBy(index => words.Items[index].Bounds.X)
                .ThenBy(index => index)
                .Take(MaximumEvidenceWordsPerNode)
                .ToArray();

            if (matched.Length == 0) continue;

            var wordIds = new List<string>(matched.Length);
            var text = new StringBuilder(Math.Min(MaximumEvidenceTextLength, matched.Length * 12));
            foreach (var index in matched)
            {
                var word = words.Items[index];
                var extraLength = word.Text.Length + (text.Length == 0 ? 0 : 1);
                if (text.Length + extraLength > MaximumEvidenceTextLength) break;
                if (text.Length != 0) text.Append(' ');
                text.Append(word.Text);
                wordIds.Add(word.Id);
                claimedWords.Add(index);
            }

            if (wordIds.Count != 0)
                result[control.StableKey] = new UiAutomationOcrEvidence(text.ToString(), wordIds);
        }

        return result;
    }

    private static bool IsEligibleControl(ScreenUiAutomationNode control) =>
        control.IsPassword != true &&
        !control.Bounds.IsEmpty &&
        !ContainerTypes.Contains(control.ControlType);

    private static bool Matches(PixelRect control, PixelRect word)
    {
        var intersection = control.Intersect(word);
        if (intersection.IsEmpty) return false;
        if (control.Contains(word.Center)) return true;
        return word.Area > 0 && intersection.Area * 100L >= word.Area * 60L;
    }

    private static WordSpatialIndex BuildWordIndex(OcrDocument ocr)
    {
        var items = new List<IndexedWord>(Math.Min(MaximumIndexedWords, 512));
        var buckets = new Dictionary<long, List<int>>();
        var largeWords = new List<int>();
        var wordOrdinal = 0;

        foreach (var line in ocr.Lines)
        {
            foreach (var sourceWord in line.Words)
            {
                wordOrdinal++;
                if (items.Count >= MaximumIndexedWords) return new WordSpatialIndex(items, buckets, largeWords);
                if (sourceWord.Bounds.IsEmpty || string.IsNullOrWhiteSpace(sourceWord.Text)) continue;

                var text = NormalizeWordText(sourceWord.Text);
                if (text.Length == 0) continue;
                var index = items.Count;
                items.Add(new IndexedWord($"w{wordOrdinal}", text, sourceWord.Bounds));

                var cells = EnumerateCells(sourceWord.Bounds, MaximumCellsPerWord + 1).ToArray();
                if (cells.Length > MaximumCellsPerWord)
                {
                    if (largeWords.Count < MaximumLargeWords) largeWords.Add(index);
                    continue;
                }

                foreach (var cell in cells)
                {
                    if (!buckets.TryGetValue(cell, out var bucket))
                    {
                        bucket = [];
                        buckets[cell] = bucket;
                    }
                    bucket.Add(index);
                }
            }
        }

        return new WordSpatialIndex(items, buckets, largeWords);
    }

    private static IEnumerable<int> QueryCandidates(PixelRect bounds, WordSpatialIndex index)
    {
        var seen = new HashSet<int>();
        foreach (var cell in EnumerateCells(bounds, 256))
        {
            if (!index.Buckets.TryGetValue(cell, out var bucket)) continue;
            foreach (var candidate in bucket)
                if (seen.Add(candidate)) yield return candidate;
        }
        foreach (var candidate in index.LargeWords)
            if (seen.Add(candidate)) yield return candidate;
    }

    private static IEnumerable<long> EnumerateCells(PixelRect bounds, int maximumCells)
    {
        if (bounds.IsEmpty || maximumCells <= 0) yield break;
        var left = FloorDiv(bounds.X, CellSize);
        var top = FloorDiv(bounds.Y, CellSize);
        var rightCoordinate = Math.Clamp((long)bounds.X + bounds.Width - 1L, int.MinValue, int.MaxValue);
        var bottomCoordinate = Math.Clamp((long)bounds.Y + bounds.Height - 1L, int.MinValue, int.MaxValue);
        var right = FloorDiv((int)rightCoordinate, CellSize);
        var bottom = FloorDiv((int)bottomCoordinate, CellSize);
        var count = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (count++ >= maximumCells) yield break;
                yield return CellKey(x, y);
            }
        }
    }

    private static int ResolveDepth(
        string key,
        IReadOnlyDictionary<string, string?> parentByKey,
        Dictionary<string, int> cache)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;
        var seen = new HashSet<string>(StringComparer.Ordinal) { key };
        var depth = 0;
        var current = key;
        while (depth < 32 && parentByKey.TryGetValue(current, out var parent) && !string.IsNullOrWhiteSpace(parent))
        {
            if (!seen.Add(parent)) break;
            depth++;
            current = parent;
        }
        cache[key] = depth;
        return depth;
    }

    private static string NormalizeWordText(string source)
    {
        var builder = new StringBuilder(Math.Min(source.Length, MaximumWordTextLength));
        foreach (var character in source.AsSpan().Trim())
        {
            if (builder.Length == MaximumWordTextLength) break;
            if (!char.IsControl(character) || character is '\t' or '\n') builder.Append(character);
        }
        return builder.ToString().Trim();
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

    private sealed record IndexedWord(string Id, string Text, PixelRect Bounds);
    private sealed record WordSpatialIndex(
        IReadOnlyList<IndexedWord> Items,
        IReadOnlyDictionary<long, List<int>> Buckets,
        IReadOnlyList<int> LargeWords);
}
