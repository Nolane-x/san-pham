using System.Security.Cryptography;

namespace Magic.Capture.Core.Utilities;

public static class HashUtility
{
    public static string ComputeSha256(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static string ComputeSha1(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
    public static string ComputeMd5(ReadOnlySpan<byte> data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    public static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
