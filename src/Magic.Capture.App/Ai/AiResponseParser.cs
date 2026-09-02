using System.Text.Json;
using System.Text.RegularExpressions;
using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai;

internal static partial class AiResponseParser
{
    [GeneratedRegex(@"\[(?<id>(?:(?:p|c\d+):)?(?:doc|[lwtsb]\d+|s\d+))\]")] private static partial Regex BracketEvidenceRegex();

    public static AiActionResult Parse(string text, string fallbackTitle)
    {
        var jsonText = ExtractJson(text);
        if (jsonText is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(jsonText);
                var root = json.RootElement;
                var title = GetString(root, "title") ?? fallbackTitle;
                var markdown = GetString(root, "markdown") ?? GetString(root, "text") ?? text;
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                if (root.TryGetProperty("fields", out var fieldObj) && fieldObj.ValueKind == JsonValueKind.Object)
                    foreach (var property in fieldObj.EnumerateObject()) fields[property.Name] = property.Value.ToString();
                var evidence = new List<string>();
                if (root.TryGetProperty("evidence", out var evidenceArray) && evidenceArray.ValueKind == JsonValueKind.Array)
                    foreach (var item in evidenceArray.EnumerateArray()) if (item.ValueKind == JsonValueKind.String && item.GetString() is { } id) evidence.Add(id);
                return new AiActionResult(title, markdown, fields, evidence, jsonText);
            }
            catch (JsonException) { }
        }

        var inferred = BracketEvidenceRegex().Matches(text).Select(m => m.Groups["id"].Value).Distinct().ToArray();
        return new AiActionResult(fallbackTitle, text, new Dictionary<string, string>(), inferred, null);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd) trimmed = trimmed[(firstLineEnd + 1)..lastFence].Trim();
        }
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : null;
    }
}
