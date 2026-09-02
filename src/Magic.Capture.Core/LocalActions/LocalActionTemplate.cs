using System.Text.RegularExpressions;

namespace Magic.Capture.Core.LocalActions;

public static partial class LocalActionTemplate
{
    [GeneratedRegex(@"(?<!\$)\$(?:\{(?<braced>[A-Za-z_][A-Za-z0-9_.-]*)\}|(?<plain>[A-Za-z_][A-Za-z0-9_.-]*))", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static string Expand(string? template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var expanded = TokenRegex().Replace(template, match =>
        {
            var key = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["plain"].Value;
            return TryGet(values, key, out var value) ? value : match.Value;
        });
        return expanded.Replace("$$", "$", StringComparison.Ordinal);
    }

    public static bool References(string? template, string variableName)
    {
        if (string.IsNullOrEmpty(template) || string.IsNullOrWhiteSpace(variableName)) return false;
        foreach (Match match in TokenRegex().Matches(template))
        {
            var key = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["plain"].Value;
            if (string.Equals(key, variableName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> values, string key, out string value)
    {
        if (values.TryGetValue(key, out value!)) return true;
        foreach (var pair in values)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
