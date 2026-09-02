namespace Magic.Capture.Core.Destinations;

public enum DestinationBodyKind { None, Json, Multipart }

public sealed record CustomHttpDestination(
    string Id,
    string Name,
    string Method,
    Uri Endpoint,
    DestinationBodyKind BodyKind,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query,
    string? BodyTemplate,
    string? FileFieldName,
    string? ResultJsonPath,
    string? SecretReference,
    bool AllowPrivateLanHttp,
    int TimeoutSeconds = 30,
    int MaxResponseBytes = 2 * 1024 * 1024,
    int SchemaVersion = 1);

public sealed record DestinationValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public static class DestinationValidator
{
    private static readonly string[] SensitiveHeaders = ["authorization", "x-api-key", "api-key", "x-auth-token", "cookie"];

    public static DestinationValidationResult Validate(CustomHttpDestination? destination)
    {
        var errors = new List<string>();
        if (destination is null) return new(false, ["Destination is required."]);
        if (string.IsNullOrWhiteSpace(destination.Id) || destination.Id.Length > 96) errors.Add("Destination id is invalid.");
        if (string.IsNullOrWhiteSpace(destination.Name) || destination.Name.Length > 120) errors.Add("Destination name is invalid.");
        if (destination.SchemaVersion != 1) errors.Add("Unsupported destination schema version.");
        if (destination.Endpoint is null || destination.Endpoint.OriginalString.Length > 2_048 || !EndpointPolicy.IsAllowed(destination.Endpoint, destination.AllowPrivateLanHttp))
            errors.Add("Endpoint must use HTTPS unless it is an explicitly allowed local endpoint, and cannot exceed 2048 characters.");

        var method = (destination.Method ?? string.Empty).Trim().ToUpperInvariant();
        if (method.Length > 16 || method is not ("GET" or "POST" or "PUT" or "PATCH")) errors.Add("HTTP method is not supported.");
        if (destination.TimeoutSeconds is < 1 or > 120) errors.Add("Timeout must be between 1 and 120 seconds.");
        if (destination.MaxResponseBytes is < 1024 or > 16 * 1024 * 1024) errors.Add("Maximum response size is outside the allowed range.");
        if (destination.BodyTemplate is { Length: > 65_536 }) errors.Add("Destination body template is too large.");
        if (destination.FileFieldName is { Length: > 256 }) errors.Add("Destination file field name is too long.");
        if (destination.ResultJsonPath is { Length: > 512 }) errors.Add("Destination result JSON path is too long.");
        if (destination.SecretReference is { Length: > 256 }) errors.Add("Destination secret reference is too long.");

        if (destination.Headers is null) errors.Add("Destination headers are required.");
        else
        {
            if (destination.Headers.Count > 64) errors.Add("Destination cannot contain more than 64 headers.");
            foreach (var (name, value) in destination.Headers.Take(65))
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 256) errors.Add("Destination contains an invalid header name.");
                if ((value ?? string.Empty).Length > 4_096) errors.Add($"Header '{name}' value is too long.");
                if (SensitiveHeaders.Contains((name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase) && !IsSecretReference(value))
                    errors.Add($"Sensitive header '{name}' must use a secret reference such as {{secret:my-key}}.");
                if (LooksLikeSecretValue(value) && !IsSecretReference(value))
                    errors.Add($"Header '{name}' appears to contain a secret and must use a secret reference.");
            }
        }

        if (destination.Query is null) errors.Add("Destination query collection is required.");
        else
        {
            if (destination.Query.Count > 64) errors.Add("Destination cannot contain more than 64 query parameters.");
            foreach (var (name, value) in destination.Query.Take(65))
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 256) errors.Add("Destination contains an invalid query name.");
                if ((value ?? string.Empty).Length > 4_096) errors.Add($"Query parameter '{name}' value is too long.");
            }
        }

        return new(errors.Count == 0, errors);
    }

    public static bool IsSecretReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("{secret:", StringComparison.OrdinalIgnoreCase) && value.EndsWith('}');

    private static bool LooksLikeSecretValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        return v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || v.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase);
    }
}
