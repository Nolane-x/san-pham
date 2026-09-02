using System.Text;

namespace Magic.Capture.Core.Utilities;

public static class GeneratedCodeInputPolicy
{
    // Keep well below theoretical QR capacity so Unicode/error-correction overhead cannot turn a
    // simple utility action into a large/slow encoder workload.
    public const int MaximumQrUtf8Bytes = 2_048;
    public const int MaximumCode128Characters = 512;

    public static string NormalizeQr(string? value)
    {
        var normalized = NormalizeRequired(value, "QR content cannot be empty.");
        if (Encoding.UTF8.GetByteCount(normalized) > MaximumQrUtf8Bytes)
            throw new ArgumentException($"QR content is too large. Maximum UTF-8 payload is {MaximumQrUtf8Bytes:N0} bytes.", nameof(value));
        return normalized;
    }

    public static string NormalizeCode128(string? value)
    {
        var normalized = NormalizeRequired(value, "Barcode content cannot be empty.");
        if (normalized.Length > MaximumCode128Characters)
            throw new ArgumentException($"Code 128 content is too large. Maximum length is {MaximumCode128Characters:N0} characters.", nameof(value));
        return normalized;
    }

    private static string NormalizeRequired(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) throw new ArgumentException(message, nameof(value));
        return normalized;
    }
}
