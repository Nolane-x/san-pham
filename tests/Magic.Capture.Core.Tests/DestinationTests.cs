using Magic.Capture.Core.Destinations;

namespace Magic.Capture.Core.Tests;

public sealed class DestinationTests
{
    [Theory]
    [InlineData("https://example.com/upload", true)]
    [InlineData("http://localhost:8080/upload", true)]
    [InlineData("http://127.0.0.1:11434/upload", true)]
    [InlineData("http://example.com/upload", false)]
    [InlineData("ftp://example.com/file", false)]
    public void Endpoint_policy_accepts_only_https_or_loopback_http(string value, bool expected)
    {
        Assert.Equal(expected, EndpointPolicy.IsAllowed(new Uri(value), allowPrivateLanHttp: false));
    }

    [Fact]
    public void Template_expander_replaces_known_tokens_and_preserves_unknown_tokens()
    {
        var result = TemplateExpander.Expand(
            "{filename}|{width}x{height}|{workflow}|{unknown}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["filename"] = "capture.png",
                ["width"] = "800",
                ["height"] = "600",
                ["workflow"] = "Quick Copy"
            });

        Assert.Equal("capture.png|800x600|Quick Copy|{unknown}", result);
    }

    [Fact]
    public void Destination_validator_rejects_plaintext_secret_values()
    {
        var destination = new CustomHttpDestination(
            "d1", "Unsafe", "POST", new Uri("https://example.com"),
            DestinationBodyKind.Json,
            new Dictionary<string, string> { ["Authorization"] = "Bearer abc" },
            new Dictionary<string, string>(),
            "{}", null, null, null, false);

        var result = DestinationValidator.Validate(destination);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("secret reference", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Destination_validator_rejects_oversized_collections_and_templates()
    {
        var headers = Enumerable.Range(0, 65).ToDictionary(i => $"X-{i}", _ => "value");
        var destination = new CustomHttpDestination(
            "d1", "Large", "POST", new Uri("https://example.com"), DestinationBodyKind.Json, headers,
            new Dictionary<string, string>(), new string('x', 65_537), null, null, null, false);
        var result = DestinationValidator.Validate(destination);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("64 headers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("template", StringComparison.OrdinalIgnoreCase));
    }
}
