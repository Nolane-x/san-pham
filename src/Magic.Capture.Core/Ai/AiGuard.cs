using System.Text.RegularExpressions;

namespace Magic.Capture.Core.Ai;

public enum AiGuardFindingKind
{
    BearerToken,
    Jwt,
    PrivateKey,
    ApiKey,
    ConnectionString,
    PasswordAssignment,
    Email,
    Phone,
    IpAddress
}

public enum AiGuardSeverity { Info, Warning, Critical }

public sealed record AiGuardFinding(AiGuardFindingKind Kind, AiGuardSeverity Severity, int Start, int Length, string Preview);

public static partial class AiGuard
{
    private sealed record Detector(AiGuardFindingKind Kind, AiGuardSeverity Severity, Regex Pattern, bool Secret);

    private static readonly Detector[] Detectors =
    [
        new(AiGuardFindingKind.PrivateKey, AiGuardSeverity.Critical, PrivateKeyRegex(), true),
        new(AiGuardFindingKind.BearerToken, AiGuardSeverity.Critical, BearerRegex(), true),
        new(AiGuardFindingKind.Jwt, AiGuardSeverity.Critical, JwtRegex(), true),
        new(AiGuardFindingKind.ConnectionString, AiGuardSeverity.Critical, ConnectionStringRegex(), true),
        new(AiGuardFindingKind.PasswordAssignment, AiGuardSeverity.Warning, PasswordRegex(), true),
        new(AiGuardFindingKind.ApiKey, AiGuardSeverity.Warning, ApiKeyRegex(), true),
        new(AiGuardFindingKind.Email, AiGuardSeverity.Info, EmailRegex(), false),
        new(AiGuardFindingKind.Phone, AiGuardSeverity.Info, PhoneRegex(), false),
        new(AiGuardFindingKind.IpAddress, AiGuardSeverity.Info, IpRegex(), false)
    ];

    public static IReadOnlyList<AiGuardFinding> Scan(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var findings = new List<AiGuardFinding>();
        foreach (var detector in Detectors)
        {
            foreach (Match match in detector.Pattern.Matches(text))
            {
                if (!match.Success || match.Length == 0) continue;
                findings.Add(new AiGuardFinding(
                    detector.Kind,
                    detector.Severity,
                    match.Index,
                    match.Length,
                    detector.Secret ? RedactedPreview(match.Value) : TruncatedPreview(match.Value)));
            }
        }
        return findings.OrderBy(f => f.Start).ThenByDescending(f => f.Severity).ToArray();
    }

    private static string RedactedPreview(string value)
    {
        var prefix = value.Split([':', '=', ' '], 2, StringSplitOptions.TrimEntries)[0];
        return string.IsNullOrWhiteSpace(prefix) ? "[redacted]" : $"{prefix}: [redacted]";
    }

    private static string TruncatedPreview(string value) => value.Length <= 48 ? value : value[..45] + "...";

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/-]{12,}={0,2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();
    [GeneratedRegex(@"(?i)\b(?:Server|Data Source)\s*=\s*[^;\r\n]+;[^\r\n]*(?:Password|Pwd)\s*=\s*[^;\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringRegex();
    [GeneratedRegex(@"(?i)\b(?:password|passwd|pwd)\s*[:=]\s*[^\s;,]{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordRegex();
    [GeneratedRegex(@"(?i)\b(?:api[_-]?key|access[_-]?key|secret[_-]?key|token)\s*[:=]\s*[A-Za-z0-9._~+/-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?<!\d)(?:\+?\d[\d ()-]{7,}\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IpRegex();
}
