using System.Text.Json;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class OpenAiCompatibleClient : AiProviderClientBase
{
    public OpenAiCompatibleClient(AiProviderProfile profile, IAiSecretStore secrets) : base(profile, secrets) { }

    public override async Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            return new(true, $"{Profile.DisplayName} connection succeeded · {models.Count} model(s) visible.");
        }
        catch (AiProviderException ex) { return new(false, ex.Message); }
    }


    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync();
        using var response = await SendAsync(() => { var r = new HttpRequestMessage(HttpMethod.Get, Endpoint("models")); Bearer(r, secret); return r; }, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("data", out var data)
            ? CollectModelNames(data, x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
            : [];
    }
    public override async Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync();
        object content = request.Prompt;
        if (request.Images.Count > 0)
        {
            var parts = new List<object> { new { type = "text", text = request.Prompt } };
            parts.AddRange(request.Images.Select(image => (object)new { type = "image_url", image_url = new { url = image.ToDataUrl() } }));
            content = parts;
        }
        var body = Profile.Capabilities.HasFlag(Magic.Capture.Core.Ai.AiCapability.StructuredJson)
            ? new { model = Profile.ModelId, messages = new[] { new { role = "user", content } }, response_format = new { type = "json_object" }, stream = false }
            : (object)new { model = Profile.ModelId, messages = new[] { new { role = "user", content } }, stream = false };
        using var response = await SendAsync(() => { var r = new HttpRequestMessage(HttpMethod.Post, Endpoint("chat/completions")); Bearer(r, secret); r.Content = JsonContent(body); return r; }, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        var text = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return new AiProviderResponse(text);
    }

    private string Endpoint(string path)
    {
        var baseUri = Profile.BaseUri.TrimEnd('/');
        return baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? Join(baseUri, path) : Join(baseUri, "v1/" + path);
    }
}
