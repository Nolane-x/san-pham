using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiGuardTests
{
    [Fact]
    public void Finds_common_cloud_transmission_risks()
    {
        const string text = "Authorization: Bearer abcdefghijklmnop\nemail=user@example.com\ntoken=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature";
        var findings = AiGuard.Scan(text);

        Assert.Contains(findings, f => f.Kind == AiGuardFindingKind.BearerToken);
        Assert.Contains(findings, f => f.Kind == AiGuardFindingKind.Email);
        Assert.Contains(findings, f => f.Kind == AiGuardFindingKind.Jwt);
    }

    [Fact]
    public void Finds_private_key_header_without_echoing_whole_secret()
    {
        var findings = AiGuard.Scan("-----BEGIN PRIVATE KEY-----\nabcdef\n-----END PRIVATE KEY-----");
        var finding = Assert.Single(findings, f => f.Kind == AiGuardFindingKind.PrivateKey);
        Assert.DoesNotContain("abcdef", finding.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Cache_key_is_stable_and_changes_with_model_or_action_revision()
    {
        var a = AiCacheKey.Create("capture", ["ctx1", "ctx2"], "action", 1, "profile", "model-a", "text");
        var b = AiCacheKey.Create("capture", ["ctx1", "ctx2"], "action", 1, "profile", "model-a", "text");
        var c = AiCacheKey.Create("capture", ["ctx1", "ctx2"], "action", 2, "profile", "model-a", "text");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }
}
