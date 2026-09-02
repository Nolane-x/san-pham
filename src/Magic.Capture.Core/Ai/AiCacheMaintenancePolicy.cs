namespace Magic.Capture.Core.Ai;

public enum AiCacheMaintenanceDecision
{
    Keep,
    DeleteInvalidName,
    DeleteKeyMismatch,
    DeleteOversize,
    DeleteExpired,
    DeleteFutureTimestamp
}

public static class AiCacheMaintenancePolicy
{
    public const long MaximumEntryJsonBytes = 8L * 1024 * 1024;
    public static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);

    public static AiCacheMaintenanceDecision Decide(
        string fileName,
        string? entryKey,
        DateTimeOffset createdUtc,
        DateTimeOffset nowUtc,
        long fileBytes,
        TimeSpan maximumAge)
    {
        if (!TryGetKeyFromFileName(fileName, out var fileKey)) return AiCacheMaintenanceDecision.DeleteInvalidName;
        if (!string.Equals(fileKey, entryKey, StringComparison.OrdinalIgnoreCase)) return AiCacheMaintenanceDecision.DeleteKeyMismatch;
        if (fileBytes <= 0 || fileBytes > MaximumEntryJsonBytes) return AiCacheMaintenanceDecision.DeleteOversize;
        if (createdUtc > nowUtc + MaximumFutureClockSkew) return AiCacheMaintenanceDecision.DeleteFutureTimestamp;
        if (maximumAge <= TimeSpan.Zero || nowUtc - createdUtc > maximumAge) return AiCacheMaintenanceDecision.DeleteExpired;
        return AiCacheMaintenanceDecision.Keep;
    }

    public static bool TryGetKeyFromFileName(string? fileName, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        var candidate = fileName[..^5];
        if (candidate.Length != 64 || !candidate.All(Uri.IsHexDigit)) return false;
        key = candidate.ToLowerInvariant();
        return true;
    }
}
