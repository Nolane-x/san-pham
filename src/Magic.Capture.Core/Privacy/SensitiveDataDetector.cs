using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Privacy;

public enum SensitiveDataKind
{
    Email,
    Phone,
    IpAddress,
    PaymentCard,
    Jwt,
    PrivateKey,
    ApiKey,
    Custom
}

public sealed record SensitivePattern(string Label, string Pattern);

public sealed record SensitiveDataOptions(
    IReadOnlyList<SensitivePattern>? CustomPatterns = null,
    IReadOnlyList<string>? SensitiveWords = null);

public sealed record SensitiveFinding(
    SensitiveDataKind Kind,
    string Value,
    PixelRect Bounds,
    double Confidence,
    string? SourceNodeId = null,
    string? Label = null);

public static class SensitiveDataDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly Regex Email = new(@"(?<![\w.+-])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,63}(?![\w.-])", Options, RegexTimeout);
    private static readonly Regex Ip = new(@"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)", Options, RegexTimeout);
    private static readonly Regex Ipv6 = new(@"(?<![0-9A-F:])(?:[0-9A-F]{0,4}:){2,7}[0-9A-F]{0,4}(?![0-9A-F:])", Options, RegexTimeout);
    private static readonly Regex Card = new(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)", Options, RegexTimeout);
    private static readonly Regex Jwt = new(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9_-])", Options, RegexTimeout);
    private static readonly Regex PrivateKey = new(@"-----BEGIN(?: [A-Z0-9]+)? PRIVATE KEY-----", Options, RegexTimeout);
    private static readonly Regex ApiKey = new(@"\b(?:sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|AIza[A-Za-z0-9_-]{20,})\b", Options, RegexTimeout);
    private static readonly Regex Phone = new(@"(?<!\w)(?:\+?\d{1,3}[ .-]?)?(?:\(?\d{2,4}\)?[ .-]?)?\d{3,4}[ .-]\d{3,4}(?!\w)", Options, RegexTimeout);

    public static IReadOnlyList<SensitiveFinding> Scan(ScreenGraphDocument graph, SensitiveDataOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new SensitiveDataOptions();
        var findings = new List<SensitiveFinding>();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text)) continue;
            AddDirectSignal(node, findings, dedupe);
            ScanRegex(node, node.Text!, Email, SensitiveDataKind.Email, .98, null, findings, dedupe);
            ScanRegex(node, node.Text!, Ip, SensitiveDataKind.IpAddress, .98, value => IsValidIp(value), findings, dedupe);
            ScanRegex(node, node.Text!, Ipv6, SensitiveDataKind.IpAddress, .98, value => IsValidIpv6(value), findings, dedupe);
            ScanRegex(node, node.Text!, Card, SensitiveDataKind.PaymentCard, .99, value => Luhn(value), findings, dedupe);
            ScanRegex(node, node.Text!, Jwt, SensitiveDataKind.Jwt, .99, null, findings, dedupe);
            ScanRegex(node, node.Text!, PrivateKey, SensitiveDataKind.PrivateKey, 1, null, findings, dedupe);
            ScanRegex(node, node.Text!, ApiKey, SensitiveDataKind.ApiKey, .99, null, findings, dedupe);
            ScanRegex(node, node.Text!, Phone, SensitiveDataKind.Phone, .82, value => !LooksLikeCard(value), findings, dedupe);

            foreach (var word in options.SensitiveWords ?? [])
            {
                if (string.IsNullOrWhiteSpace(word)) continue;
                var normalizedWord = word.Trim();
                if (normalizedWord.Length is < 2 or > 128) continue;
                var index = node.Text!.IndexOf(normalizedWord, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var matched = node.Text.Substring(index, normalizedWord.Length);
                    Add(findings, dedupe, SensitiveDataKind.Custom, matched, node, .99, "Sensitive word");
                }
            }

            foreach (var custom in options.CustomPatterns ?? [])
            {
                if (string.IsNullOrWhiteSpace(custom.Label) || string.IsNullOrWhiteSpace(custom.Pattern) || custom.Pattern.Length > 512) continue;
                try
                {
                    var regex = new Regex(custom.Pattern, Options, RegexTimeout);
                    foreach (Match match in regex.Matches(node.Text!))
                        Add(findings, dedupe, SensitiveDataKind.Custom, match.Value, node, .95, custom.Label);
                }
                catch (ArgumentException)
                {
                    // Invalid user patterns are ignored by the detector; validation UI can surface them separately.
                }
                catch (RegexMatchTimeoutException)
                {
                    // A pathological custom regex must never stall capture processing.
                }
            }
        }
        return findings;
    }

    private static void AddDirectSignal(ScreenGraphNode node, List<SensitiveFinding> findings, HashSet<string> dedupe)
    {
        var kind = node.Kind switch
        {
            ScreenNodeKind.Email => SensitiveDataKind.Email,
            ScreenNodeKind.Phone => SensitiveDataKind.Phone,
            ScreenNodeKind.IpAddress => SensitiveDataKind.IpAddress,
            _ => (SensitiveDataKind?)null
        };
        if (kind is { } mapped) Add(findings, dedupe, mapped, node.Text!, node, Math.Max(.9, node.Confidence), null);
    }

    private static void ScanRegex(ScreenGraphNode node, string text, Regex regex, SensitiveDataKind kind, double confidence,
        Func<string, bool>? predicate, List<SensitiveFinding> findings, HashSet<string> dedupe)
    {
        try
        {
            foreach (Match match in regex.Matches(text))
            {
                if (predicate is not null && !predicate(match.Value)) continue;
                Add(findings, dedupe, kind, match.Value, node, confidence, null);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Built-in scans are bounded; timeout means skip this pattern for the current node.
        }
    }

    private static void Add(List<SensitiveFinding> findings, HashSet<string> dedupe, SensitiveDataKind kind, string value,
        ScreenGraphNode node, double confidence, string? label)
    {
        var normalized = value.Trim();
        var key = $"{kind}|{node.Id}|{normalized}";
        if (!dedupe.Add(key)) return;
        findings.Add(new SensitiveFinding(kind, normalized, node.Bounds, confidence, node.Id, label));
    }

    private static bool IsValidIp(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part => byte.TryParse(part, out _));
    }

    private static bool IsValidIpv6(string value) =>
        IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;

    private static bool LooksLikeCard(string value)
    {
        var digits = value.Where(char.IsDigit).ToArray();
        return digits.Length is >= 13 and <= 19;
    }

    internal static bool Luhn(string value)
    {
        var digits = value.Where(char.IsDigit).Select(ch => ch - '0').ToArray();
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        var parity = digits.Length % 2;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[i];
            if (i % 2 == parity)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }
}
