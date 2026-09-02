using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Privacy;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Tests;

public sealed class SensitiveDataDetectorTests
{
    [Fact]
    public void Detector_finds_email_ip_valid_card_jwt_and_private_key_marker()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature_part";
        var graph = Graph($"mail me at jane@example.com from 192.168.1.20 card 4111 1111 1111 1111 token {jwt} -----BEGIN PRIVATE KEY-----");
        var findings = SensitiveDataDetector.Scan(graph);
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.Email);
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.IpAddress);
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.PaymentCard);
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.Jwt);
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.PrivateKey);
    }

    [Fact]
    public void Detector_rejects_card_like_numbers_that_fail_luhn()
    {
        var findings = SensitiveDataDetector.Scan(Graph("card 4111 1111 1111 1112"));
        Assert.DoesNotContain(findings, f => f.Kind == SensitiveDataKind.PaymentCard);
    }

    [Fact]
    public void Custom_pattern_is_bounded_and_gets_source_bounds()
    {
        var graph = Graph("customer secret PROJECT-8842");
        var options = new SensitiveDataOptions([new SensitivePattern("project", @"PROJECT-\d{4}")]);
        var finding = Assert.Single(SensitiveDataDetector.Scan(graph, options), x => x.Kind == SensitiveDataKind.Custom);
        Assert.Equal(new PixelRect(10, 20, 300, 30), finding.Bounds);
        Assert.Equal("project", finding.Label);
    }


    [Fact]
    public void Detector_finds_ipv6_and_user_defined_sensitive_words()
    {
        var graph = Graph("server 2001:db8::8a2e:370:7334 customer ALPHA-SECRET");
        var options = new SensitiveDataOptions(SensitiveWords: ["alpha-secret"]);
        var findings = SensitiveDataDetector.Scan(graph, options);
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.IpAddress && f.Value.Contains(':'));
        Assert.Contains(findings, f => f.Kind == SensitiveDataKind.Custom && f.Label == "Sensitive word" && f.Value == "ALPHA-SECRET");
    }

    private static ScreenGraphDocument Graph(string text) => new(
        1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, 400, 200, "Region", null,
        [new ScreenGraphNode("l1", ScreenNodeKind.TextLine, text, new PixelRect(10, 20, 300, 30), .98, "doc", null)]);
}
