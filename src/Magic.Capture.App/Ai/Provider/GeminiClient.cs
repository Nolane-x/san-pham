using System.Text.Json;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class GeminiClient : AiProviderClientBase
{
    public GeminiClient(AiProviderProfile profile, IAiSecretStore secrets) : base(profile, secrets) { }

    private async Task<string> SecretAsync() => await GetSecretAsync() ?? throw new AiProviderException(Profile.DisplayName, "API key is not configured.");

    public override async Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            return new(true, $"Gemini connection succeeded · {models.Count} model(s) visible.");
        }
        catch (AiProviderException ex) { return new(false, ex.Message); }
    }


    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var key = await SecretAsync();
        using var response = await SendAsync(() => Create(HttpMethod.Get, Join(Profile.BaseUri, "models"), key, null), cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("models", out var models)
            ? CollectModelNames(models, x => x.TryGetProperty("name", out var name) ? name.GetString()?.Replace("models/", string.Empty, StringComparison.Ordinal) : null)
            : [];
    }
    public override async Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var key = await SecretAsync();
        var parts = new List<object>();
        foreach (var image in request.Images)
            parts.Add(new { inlineData = new { mimeType = image.MimeType, data = Convert.ToBase64String(image.Bytes) } });
        parts.Add(new { text = request.Prompt });
        object body = Profile.Capabilities.HasFlag(Magic.Capture.Core.Ai.AiCapability.StructuredJson)
            ? new { contents = new[] { new { role = "user", parts } }, generationConfig = new { responseMimeType = "application/json" } }
            : new { contents = new[] { new { role = "user", parts } } };
        var uri = Join(Profile.BaseUri, $"models/{Uri.EscapeDataString(Profile.ModelId)}:generateContent");
        using var response = await SendAsync(() => Create(HttpMethod.Post, uri, key, body), cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        var texts = new List<string>();
        if (json.RootElement.TryGetProperty("candidates", out var candidates))
            foreach (var candidate in candidates.EnumerateArray())
                if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var outputParts))
                    foreach (var part in outputParts.EnumerateArray())
                        if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) texts.Add(text.GetString() ?? string.Empty);
        return new AiProviderResponse(string.Join("\n", texts));
    }

    private static HttpRequestMessage Create(HttpMethod method, string uri, string key, object? body)
    {
        var r = new HttpRequestMessage(method, uri);
        r.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        if (body is not null) r.Content = JsonContent(body);
        return r;
    }
}
