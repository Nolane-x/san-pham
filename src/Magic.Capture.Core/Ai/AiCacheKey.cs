using System.Security.Cryptography;
using System.Text;

namespace Magic.Capture.Core.Ai;

public static class AiCacheKey
{
    public static string Create(
        string captureHash,
        IReadOnlyList<string> contextHashes,
        string actionId,
        int actionRevision,
        string providerProfileId,
        string modelId,
        string inputStrategy)
    {
        var builder = new StringBuilder();
        Append(builder, captureHash);
        foreach (var hash in contextHashes) Append(builder, hash);
        Append(builder, actionId);
        Append(builder, actionRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, providerProfileId);
        Append(builder, modelId);
        Append(builder, inputStrategy);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length).Append(':').Append(value).Append('|');
    }
}
