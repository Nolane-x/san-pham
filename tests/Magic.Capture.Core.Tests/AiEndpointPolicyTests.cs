using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiEndpointPolicyTests
{
    [Theory]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("http://[::1]:8000/v1", true)]
    [InlineData("http://example.com/v1", false)]
    [InlineData("ftp://localhost/model", false)]
    public void Only_https_or_loopback_http_is_allowed(string endpoint, bool expected)
    {
        Assert.Equal(expected, AiEndpointPolicy.IsAllowed(new Uri(endpoint)));
    }

    [Fact]
    public void Invalid_absolute_uri_is_rejected()
    {
        Assert.False(AiEndpointPolicy.TryValidate("not-a-uri", out _));
    }
}
