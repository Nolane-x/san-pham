using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Magic.Capture.App.Capture;
using Magic.Capture.Core.Destinations;

namespace Magic.Capture.App.Destinations;

internal sealed record DestinationRequestContext(
    CaptureAsset Asset,
    string FileName,
    IReadOnlyDictionary<string, string> Values);

internal sealed record DestinationResponse(
    int StatusCode,
    string? ResultUrl,
    string ResponseText,
    IReadOnlyDictionary<string, string> Headers);

internal sealed partial class CustomHttpDestinationClient
{
    private static readonly HttpClient Http = new();
    private readonly IDestinationSecretStore _secrets;
    public CustomHttpDestinationClient(IDestinationSecretStore secrets) => _secrets = secrets;

    public async Task<DestinationResponse> SendAsync(CustomHttpDestination destination, DestinationRequestContext context, CancellationToken cancellationToken = default)
    {
        var validation = DestinationValidator.Validate(destination);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));

        var endpoint = BuildUri(destination, context.Values);
        using var request = new HttpRequestMessage(new HttpMethod(destination.Method.Trim().ToUpperInvariant()), endpoint);
        foreach (var (name, valueTemplate) in destination.Headers)
        {
            var value = await ResolveSecretsAsync(TemplateExpander.Expand(valueTemplate, context.Values));
            if (!request.Headers.TryAddWithoutValidation(name, value) && request.Content is not null)
                request.Content.Headers.TryAddWithoutValidation(name, value);
        }

        request.Content = destination.BodyKind switch
        {
            DestinationBodyKind.Json => new StringContent(TemplateExpander.Expand(destination.BodyTemplate ?? "{}", context.Values), Encoding.UTF8, "application/json"),
            DestinationBodyKind.Multipart => BuildMultipart(destination, context),
            _ => null
        };

        // Content headers supplied by profile are applied after content creation.
        foreach (var (name, valueTemplate) in destination.Headers)
        {
            if (request.Headers.Contains(name) || request.Content is null) continue;
            var value = await ResolveSecretsAsync(TemplateExpander.Expand(valueTemplate, context.Values));
            request.Content.Headers.TryAddWithoutValidation(name, value);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(destination.TimeoutSeconds, 1, 120)));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var responseText = await ReadBoundedAsync(response, destination.MaxResponseBytes, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Destination returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(x => x.Value)), StringComparer.OrdinalIgnoreCase);
        var resultUrl = ExtractResultUrl(destination, responseText, headers);
        return new DestinationResponse((int)response.StatusCode, resultUrl, responseText, headers);
    }

    private static Uri BuildUri(CustomHttpDestination destination, IReadOnlyDictionary<string, string> values)
    {
        var builder = new UriBuilder(destination.Endpoint);
        var pairs = new List<string>();
        if (!string.IsNullOrWhiteSpace(builder.Query)) pairs.Add(builder.Query.TrimStart('?'));
        pairs.AddRange(destination.Query.Select(kvp =>
            $"{Uri.EscapeDataString(TemplateExpander.Expand(kvp.Key, values))}={Uri.EscapeDataString(TemplateExpander.Expand(kvp.Value, values))}"));
        builder.Query = string.Join("&", pairs.Where(x => !string.IsNullOrWhiteSpace(x)));
        return builder.Uri;
    }

    private static HttpContent BuildMultipart(CustomHttpDestination destination, DestinationRequestContext context)
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(context.Asset.PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(file, destination.FileFieldName ?? "file", context.FileName);
        if (!string.IsNullOrWhiteSpace(destination.BodyTemplate))
            multipart.Add(new StringContent(TemplateExpander.Expand(destination.BodyTemplate, context.Values), Encoding.UTF8), "metadata");
        return multipart;
    }

    private async Task<string> ResolveSecretsAsync(string value)
    {
        var matches = SecretTokenRegex().Matches(value).Cast<Match>().ToArray();
        foreach (var match in matches)
        {
            var id = match.Groups[1].Value;
            var secret = await _secrets.GetAsync(id) ?? throw new InvalidOperationException($"Destination secret '{id}' is not available.");
            value = value.Replace(match.Value, secret, StringComparison.Ordinal);
        }
        return value;
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        maxBytes = Math.Clamp(maxBytes, 1024, 16 * 1024 * 1024);
        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
            throw new InvalidOperationException("Destination response exceeded the configured safety limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var rented = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await input.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maxBytes) throw new InvalidOperationException("Destination response exceeded the configured safety limit.");
                await output.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string? ExtractResultUrl(CustomHttpDestination destination, string body, IReadOnlyDictionary<string, string> headers)
    {
        if (!string.IsNullOrWhiteSpace(destination.ResultJsonPath))
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                JsonElement current = json.RootElement;
                foreach (var part in destination.ResultJsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return null;
                }
                if (current.ValueKind == JsonValueKind.String) return current.GetString();
            }
            catch (JsonException) { }
        }
        if (headers.TryGetValue("Location", out var location) && Uri.TryCreate(location, UriKind.Absolute, out _)) return location;
        var trimmed = body.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.ToString() : null;
    }

    [GeneratedRegex(@"\{secret:([A-Za-z0-9_.-]{1,80})\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretTokenRegex();
}
