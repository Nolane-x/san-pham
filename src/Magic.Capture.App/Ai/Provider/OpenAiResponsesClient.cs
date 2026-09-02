using System.Text.Json;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class OpenAiResponsesClient : AiProviderClientBase
{
    public OpenAiResponsesClient(AiProviderProfile profile, IAiSecretStore secrets) : base(profile, secrets) { }

    public override async Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync();
        if (string.IsNullOrWhiteSpace(secret)) return new(false, "API key is not configured.");
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            return new(true, $"OpenAI connection succeeded · {models.Count} model(s) visible.");
        }
        catch (AiProviderException ex) { return new(false, ex.Message); }
    }


    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync() ?? throw new AiProviderException(Profile.DisplayName, "API key is not configured.");
        using var response = await SendAsync(() => { var r = new HttpRequestMessage(HttpMethod.Get, Join(Profile.BaseUri, "v1/models")); Bearer(r, secret); return r; }, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("data", out var data)
            ? CollectModelNames(data, x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
            : [];
    }
    public override async Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync() ?? throw new AiProviderException(Profile.DisplayName, "API key is not configured.");
        var content = new List<object> { new { type = "input_text", text = request.Prompt } };
        content.AddRange(request.Images.Select(image => (object)new { type = "input_image", image_url = image.ToDataUrl() }));
        object body = Profile.Capabilities.HasFlag(Magic.Capture.Core.Ai.AiCapability.StructuredJson)
            ? new { model = Profile.ModelId, input = new[] { new { role = "user", content } }, text = new { format = new { type = "json_object" } }, store = false }
            : new { model = Profile.ModelId, input = new[] { new { role = "user", content } }, store = false };
        using var response = await SendAsync(() => { var r = new HttpRequestMessage(HttpMethod.Post, Join(Profile.BaseUri, "v1/responses")); Bearer(r, secret); r.Content = JsonContent(body); return r; }, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        var text = ExtractOutputText(json.RootElement);
        return new AiProviderResponse(text);
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String) return outputText.GetString() ?? string.Empty;
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return string.Empty;
        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
            if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var block in content.EnumerateArray())
                    if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) parts.Add(text.GetString() ?? string.Empty);
        return string.Join("\n", parts);
    }
}
