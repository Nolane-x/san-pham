using System.Text.Json;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class AnthropicMessagesClient : AiProviderClientBase
{
    public AnthropicMessagesClient(AiProviderProfile profile, IAiSecretStore secrets) : base(profile, secrets) { }

    private async Task<string> SecretAsync() => await GetSecretAsync() ?? throw new AiProviderException(Profile.DisplayName, "API key is not configured.");

    public override async Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            return new(true, $"Anthropic connection succeeded · {models.Count} model(s) visible.");
        }
        catch (AiProviderException ex) { return new(false, ex.Message); }
    }


    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var key = await SecretAsync();
        using var response = await SendAsync(() => Create(HttpMethod.Get, Join(Profile.BaseUri, "v1/models"), key, null), cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("data", out var data)
            ? CollectModelNames(data, x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
            : [];
    }
    public override async Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var key = await SecretAsync();
        var blocks = new List<object>();
        foreach (var image in request.Images)
            blocks.Add(new { type = "image", source = new { type = "base64", media_type = image.MimeType, data = Convert.ToBase64String(image.Bytes) } });
        blocks.Add(new { type = "text", text = request.Prompt });
        var body = new { model = Profile.ModelId, max_tokens = 4096, messages = new[] { new { role = "user", content = blocks } } };
        using var response = await SendAsync(() => Create(HttpMethod.Post, Join(Profile.BaseUri, "v1/messages"), key, body), cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        var parts = new List<string>();
        if (json.RootElement.TryGetProperty("content", out var content))
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) parts.Add(text.GetString() ?? string.Empty);
        return new AiProviderResponse(string.Join("\n", parts));
    }

    private static HttpRequestMessage Create(HttpMethod method, string uri, string key, object? body)
    {
        var r = new HttpRequestMessage(method, uri);
        r.Headers.TryAddWithoutValidation("x-api-key", key);
        r.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        if (body is not null) r.Content = JsonContent(body);
        return r;
    }
}
