using System.Text.Json;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class OllamaClient : AiProviderClientBase
{
    public OllamaClient(AiProviderProfile profile, IAiSecretStore secrets) : base(profile, secrets) { }

    public override async Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            return new(true, $"Ollama connection succeeded · {models.Count} local model(s) visible.");
        }
        catch (AiProviderException ex) { return new(false, ex.Message); }
    }


    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Join(Profile.BaseUri, "api/tags")), cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("models", out var models)
            ? CollectModelNames(models, x => x.TryGetProperty("name", out var name) ? name.GetString() : null)
            : [];
    }
    public override async Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var message = request.Images.Count == 0
            ? (object)new { role = "user", content = request.Prompt }
            : new { role = "user", content = request.Prompt, images = request.Images.Select(i => Convert.ToBase64String(i.Bytes)).ToArray() };
        object body = Profile.Capabilities.HasFlag(Magic.Capture.Core.Ai.AiCapability.StructuredJson)
            ? new { model = Profile.ModelId, messages = new[] { message }, stream = false, format = "json" }
            : new { model = Profile.ModelId, messages = new[] { message }, stream = false };
        using var response = await SendAsync(() => { var r = new HttpRequestMessage(HttpMethod.Post, Join(Profile.BaseUri, "api/chat")); r.Content = JsonContent(body); return r; }, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        var text = json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return new AiProviderResponse(text);
    }
}
