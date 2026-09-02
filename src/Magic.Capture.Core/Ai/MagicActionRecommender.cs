using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Ai;

public sealed record MagicActionRecommendation(string ActionId, int Score, string Reason);

public static class MagicActionRecommender
{
    public static IReadOnlyList<MagicActionRecommendation> Recommend(ScreenGraphDocument graph)
    {
        var scores = new Dictionary<string, MagicActionRecommendation>(StringComparer.Ordinal);
        void Add(string id, int score, string reason)
        {
            if (!scores.TryGetValue(id, out var current) || score > current.Score)
                scores[id] = new MagicActionRecommendation(id, score, reason);
        }

        var kinds = graph.Nodes.Select(n => n.Kind).ToHashSet();
        var hasError = kinds.Contains(ScreenNodeKind.Error) || kinds.Contains(ScreenNodeKind.ErrorCode);
        var hasStack = kinds.Contains(ScreenNodeKind.StackFrame) || kinds.Contains(ScreenNodeKind.LineReference) || kinds.Contains(ScreenNodeKind.FilePath);
        var hasTable = kinds.Contains(ScreenNodeKind.Table);
        var hasCode = kinds.Contains(ScreenNodeKind.CodeLike) || hasStack;
        var textLength = graph.Nodes.Where(n => n.Kind is ScreenNodeKind.TextLine or ScreenNodeKind.Word).Sum(n => n.Text?.Length ?? 0);
        var hasEntities = kinds.Overlaps([ScreenNodeKind.Url, ScreenNodeKind.Email, ScreenNodeKind.Phone, ScreenNodeKind.Money, ScreenNodeKind.Percentage]);

        if (hasError)
        {
            Add("developer.explain-error", 100, "Error text detected deterministically.");
            Add("developer.bug-report", 94, "Error evidence can be turned into a structured report.");
            Add("developer.causes", 88, "Error evidence is available for grounded hypotheses.");
        }
        if (hasStack)
        {
            Add("developer.stack-trace", 96, "Stack frames or source locations were detected.");
            Add("developer.debug-checklist", 84, "Source/error locations can ground debugging steps.");
        }
        if (hasCode)
        {
            Add("developer.explain-code", hasError ? 82 : 92, "Code-like text or source references were detected.");
            Add("developer.find-bug", hasError ? 86 : 89, "Code-like text can be reviewed for likely defects.");
            Add("developer.test-ideas", 76, "Visible code can ground test ideas.");
        }
        if (hasTable)
        {
            Add("data.explain-table", 100, "A table was reconstructed from OCR geometry.");
            Add("data.records", 95, "The detected table can be transformed into structured records.");
            Add("data.anomalies", 88, "Table values can be checked for anomalies.");
            Add("data.trends", 84, "Table values may support trend analysis.");
        }
        if (hasEntities)
        {
            Add("document.entities", 87, "Structured values such as URLs, contacts or numeric signals were detected.");
            Add("general.key-facts", 80, "Deterministic signals can anchor key-fact extraction.");
        }

        if (textLength >= 80) Add("general.summarize", 75, "Substantial OCR text is available.");
        else Add("general.explain", 72, "A concise explanation is broadly useful for this capture.");
        Add("general.explain", 70, "General fallback.");
        Add("general.summarize", 68, "General fallback for visible text.");
        Add("general.translate", 55, "Translation is always available for recognized text.");
        Add("general.ask", 50, "Ask a custom grounded question.");

        return scores.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ActionId, StringComparer.Ordinal)
            .ToArray();
    }
}
