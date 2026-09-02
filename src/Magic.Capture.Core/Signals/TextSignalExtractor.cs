using System.Text.RegularExpressions;
using Magic.Capture.Core.Ocr;

namespace Magic.Capture.Core.Signals;

public static partial class TextSignalExtractor
{
    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)] private static partial Regex UrlRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)] private static partial Regex EmailRegex();
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")] private static partial Regex IpRegex();
    [GeneratedRegex(@"(?:[A-Za-z]:\\|/)[^\r\n:*?""<>|]+(?:\.[A-Za-z0-9]{1,8})?(?::line\s*\d+|:\d+(?::\d+)?)?", RegexOptions.IgnoreCase)] private static partial Regex PathRegex();
    [GeneratedRegex(@"\b(?:line\s+\d+|L\d+(?::\d+)?|:\d+(?::\d+)?)\b", RegexOptions.IgnoreCase)] private static partial Regex LineRegex();
    [GeneratedRegex(@"\b(?:[A-Z][A-Za-z0-9_.]*(?:Exception|Error)|(?:ERROR|Error|error)\s*[A-Z0-9_-]*)\b")] private static partial Regex ErrorRegex();
    [GeneratedRegex(@"\b(?:0x[0-9A-F]+|[A-Z]{2,}[0-9]{3,}|E[0-9]{3,})\b", RegexOptions.IgnoreCase)] private static partial Regex ErrorCodeRegex();
    [GeneratedRegex(@"(?<!\w)(?:[$€£¥₫]\s?\d[\d,.]*|\d[\d,.]*\s?(?:USD|EUR|GBP|JPY|VND))(?!\w)", RegexOptions.IgnoreCase)] private static partial Regex MoneyRegex();
    [GeneratedRegex(@"\b\d+(?:\.\d+)?%\b")] private static partial Regex PercentRegex();
    [GeneratedRegex(@"\+?\d[\d\s().-]{7,}\d")] private static partial Regex PhoneRegex();

    public static IReadOnlyList<TextSignal> Extract(OcrDocument document)
    {
        var results = new List<TextSignal>();
        foreach (var line in document.Lines)
        {
            AddMatches(results, line, UrlRegex(), TextSignalKind.Url, .99);
            AddMatches(results, line, EmailRegex(), TextSignalKind.Email, .99);
            AddMatches(results, line, IpRegex(), TextSignalKind.IpAddress, .95);
            AddMatches(results, line, PathRegex(), TextSignalKind.FilePath, .92);
            AddMatches(results, line, LineRegex(), TextSignalKind.LineReference, .85);
            AddMatches(results, line, ErrorCodeRegex(), TextSignalKind.ErrorCode, .82);
            AddMatches(results, line, MoneyRegex(), TextSignalKind.Money, .93);
            AddMatches(results, line, PercentRegex(), TextSignalKind.Percentage, .98);
            AddMatches(results, line, PhoneRegex(), TextSignalKind.Phone, .72);

            var trimmed = line.Text.Trim();
            if (trimmed.StartsWith("at ", StringComparison.OrdinalIgnoreCase) || trimmed.Contains(" in ") && trimmed.Contains(":line "))
                results.Add(new TextSignal(TextSignalKind.StackFrame, trimmed, line.Bounds, .96));
            if (ErrorRegex().IsMatch(trimmed) && (trimmed.Contains(':') || trimmed.EndsWith("Exception", StringComparison.OrdinalIgnoreCase)))
                results.Add(new TextSignal(TextSignalKind.ErrorHeadline, trimmed, line.Bounds, .9));
            if (LooksLikeCode(trimmed))
                results.Add(new TextSignal(TextSignalKind.CodeLike, trimmed, line.Bounds, .65));
        }

        return results
            .DistinctBy(s => (s.Kind, s.Value, s.Bounds))
            .ToArray();
    }

    private static void AddMatches(List<TextSignal> target, OcrLine line, Regex regex, TextSignalKind kind, double confidence)
    {
        foreach (Match match in regex.Matches(line.Text))
            target.Add(new TextSignal(kind, match.Value.TrimEnd('.', ',', ';', ')'), line.Bounds, confidence));
    }

    private static bool LooksLikeCode(string text)
    {
        if (text.Length < 8) return false;
        var score = 0;
        if (text.Contains("=>") || text.Contains("==") || text.Contains("!=") || text.Contains("::")) score += 2;
        if (text.Contains('{') || text.Contains('}') || text.Contains(';')) score++;
        if (text.Contains("public ") || text.Contains("private ") || text.Contains("def ") || text.Contains("function ") || text.Contains("const ") || text.Contains("let ")) score += 2;
        return score >= 2;
    }
}
