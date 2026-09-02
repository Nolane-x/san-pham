using System.Net;
using System.Net.Sockets;

namespace Magic.Capture.Core.Destinations;

public static class EndpointPolicy
{
    public static bool IsAllowed(Uri? endpoint, bool allowPrivateLanHttp)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri) return false;
        if (endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;
        if (!endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) return false;
        if (IsLoopback(endpoint.Host)) return true;
        return allowPrivateLanHttp && IsPrivateIpLiteral(endpoint.Host);
    }

    public static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

    private static bool IsPrivateIpLiteral(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
