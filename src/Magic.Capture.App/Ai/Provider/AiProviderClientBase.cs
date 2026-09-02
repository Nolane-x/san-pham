using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Buffers;
using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai.Provider;

internal abstract class AiProviderClientBase : IAiProviderClient
{
    protected static readonly HttpClient Http = new();
    protected readonly IAiSecretStore Secrets;
    public AiProviderProfile Profile { get; }

    protected AiProviderClientBase(AiProviderProfile profile, IAiSecretStore secrets)
    {
        if (!AiEndpointPolicy.TryValidate(profile.BaseUri, out _)) throw new AiProviderException(profile.DisplayName, AiEndpointPolicy.ErrorMessage);
        Profile = profile;
        Secrets = secrets;
    }

    public abstract Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default);
    public abstract Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);
    public abstract Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default);

    protected async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Profile.TimeoutSeconds, 10, 600)));
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = requestFactory();
            try { response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AiProviderException(Profile.DisplayName, "Request timed out."); }
            catch (HttpRequestException ex) { throw new AiProviderException(Profile.DisplayName, ex.Message); }

            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            var retryable = response.StatusCode == HttpStatusCode.RequestTimeout || response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
            if (retryable && attempt == 0) { response.Dispose(); await Task.Delay(350, cancellationToken); continue; }
            var safe = response.ReasonPhrase ?? "Request failed";
            response.Dispose();
            throw new AiProviderException(Profile.DisplayName, safe, status);
        }
        throw new AiProviderException(Profile.DisplayName, "Request failed.");
    }

    protected async Task<string?> GetSecretAsync()
    {
        if (Profile.IsLocal && string.IsNullOrWhiteSpace(Profile.SecretId)) return null;
        return await Secrets.GetAsync(Profile.SecretId);
    }

    protected static StringContent JsonContent(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    protected static void Bearer(HttpRequestMessage request, string? secret) { if (!string.IsNullOrWhiteSpace(secret)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret); }
    protected static string Join(string baseUri, string path) => baseUri.TrimEnd('/') + "/" + path.TrimStart('/');

    protected static IReadOnlyList<string> CollectModelNames(JsonElement array, Func<JsonElement, string?> selector)
    {
        if (array.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var models = new List<string>(Math.Min(array.GetArrayLength(), AiModelListPolicy.MaximumModels));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in array.EnumerateArray())
        {
            if (!AiModelListPolicy.Accept(selector(element), out var model) || !seen.Add(model)) continue;
            models.Add(model);
            if (models.Count >= AiModelListPolicy.MaximumModels) break;
        }
        models.Sort(StringComparer.Ordinal);
        return models;
    }

    protected async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maxResponseBytes = 16 * 1024 * 1024;
        if (response.Content.Headers.ContentLength is long declared && declared > maxResponseBytes)
            throw new AiProviderException(Profile.DisplayName, $"Response exceeded the {maxResponseBytes / (1024 * 1024)} MB safety limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(capacity: (int)Math.Min(response.Content.Headers.ContentLength ?? 64 * 1024, maxResponseBytes));
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maxResponseBytes)
                    throw new AiProviderException(Profile.DisplayName, $"Response exceeded the {maxResponseBytes / (1024 * 1024)} MB safety limit.");
                await buffer.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }
}
