namespace Magic.Capture.Core.History;

/// <summary>
/// In-memory inverted index for History metadata/OCR previews. It is deliberately derived state:
/// rebuilding it is always safe and never changes primary capture data.
/// </summary>
public sealed class HistoryTextIndex
{
    private const int MaximumVocabularyTokens = 1_000_000;
    private const int MaximumTokenLength = 240;
    private readonly IReadOnlyDictionary<string, Guid[]> _postings;
    private readonly Guid[] _allIds;

    private HistoryTextIndex(IReadOnlyDictionary<string, Guid[]> postings, Guid[] allIds)
    {
        _postings = postings;
        _allIds = allIds;
    }

    public static HistoryTextIndex Build(IEnumerable<HistoryItem>? items)
    {
        var postings = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        var all = new HashSet<Guid>();

        foreach (var item in items ?? [])
        {
            if (item is null || item.Id == Guid.Empty) continue;
            all.Add(item.Id);
            foreach (var token in Tokenize(HistorySearch.GetSearchableText(item)))
            {
                if (!postings.TryGetValue(token, out var ids))
                {
                    if (postings.Count >= MaximumVocabularyTokens) continue;
                    postings[token] = ids = [];
                }
                ids.Add(item.Id);
            }
        }

        var frozen = postings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(id => id).ToArray(),
            StringComparer.Ordinal);
        return new HistoryTextIndex(frozen, all.OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// Returns a conservative candidate set. Final correctness must still be checked with
    /// <see cref="HistorySearch.Matches"/> because that predicate intentionally supports arbitrary
    /// substring matching across the original searchable text.
    /// </summary>
    public IReadOnlyList<Guid> Search(string? query)
    {
        var terms = SplitQuery(query).ToArray();
        if (terms.Length == 0) return _allIds;

        HashSet<Guid>? candidates = null;
        foreach (var term in terms)
        {
            var termIds = new HashSet<Guid>();
            foreach (var pair in _postings)
            {
                if (!pair.Key.Contains(term, StringComparison.Ordinal)) continue;
                termIds.UnionWith(pair.Value);
            }

            if (candidates is null) candidates = termIds;
            else candidates.IntersectWith(termIds);
            if (candidates.Count == 0) break;
        }

        return (candidates ?? []).OrderBy(id => id).ToArray();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = Normalize(raw);
            if (token.Length > 0) yield return token;
        }
    }

    private static IEnumerable<string> SplitQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        foreach (var raw in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = Normalize(raw);
            if (token.Length > 0) yield return token;
        }
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= MaximumTokenLength ? normalized : normalized[..MaximumTokenLength];
    }
}
