using System.Text.RegularExpressions;

namespace Magic.Capture.Core.Destinations;

public static partial class TemplateExpander
{
    [GeneratedRegex(@"\{([A-Za-z0-9_.-]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static string Expand(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return TokenRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
