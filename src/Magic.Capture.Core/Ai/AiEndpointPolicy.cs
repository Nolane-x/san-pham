namespace Magic.Capture.Core.Ai;

public static class AiEndpointPolicy
{
    public static bool TryValidate(string? value, out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (!IsAllowed(parsed)) return false;
        endpoint = parsed;
        return true;
    }

    public static bool IsAllowed(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;
        if (!endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) return false;
        return endpoint.IsLoopback || endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static string ErrorMessage => "AI endpoint must use HTTPS. Plain HTTP is allowed only for localhost/loopback endpoints.";
}
