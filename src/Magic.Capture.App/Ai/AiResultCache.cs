using System.Text.Json;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai;

internal sealed record AiResultCacheEntry(
    string Key,
    DateTimeOffset CreatedUtc,
    string ActionId,
    string ProviderProfileId,
    string ModelId,
    AiActionResult Result);

internal sealed record AiCacheRepairReport(
    int Scanned,
    int Kept,
    int Deleted,
    int Invalid,
    int Expired,
    int AncillaryDeleted,
    int Failed);

internal sealed class AiResultCache
{
    private const int MaximumFilesScannedPerPrune = 20_000;
    private const int MaximumAncillaryFilesCleanedPerPrune = 512;
    private const long MaximumEntryJsonBytes = AiCacheMaintenancePolicy.MaximumEntryJsonBytes;
    private readonly AppPaths _paths;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AiResultCache(AppPaths paths, LocalLog log)
    {
        _paths = paths;
        _log = log;
    }

    public async Task<AiResultCacheEntry?> TryGetAsync(string key, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var entry = await AtomicJsonFile.ReadAsync<AiResultCacheEntry>(path, cancellationToken, MaximumEntryJsonBytes);
                if (entry is null || !string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    TryDeleteCachePath(path);
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                if (entry.CreatedUtc > now.AddMinutes(5) || now - entry.CreatedUtc > maxAge)
                {
                    TryDeleteCachePath(path);
                    return null;
                }
                return entry;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsExpectedCachePersistenceFailure(ex))
            {
                _log.Error("AiCacheRead", ex);
                TryDeleteCachePath(path);
                return null;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TryPutAsync(AiResultCacheEntry entry, int maximumEntries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(entry.Key);
            try
            {
                await AtomicJsonFile.WriteAsync(path, entry, cancellationToken, MaximumEntryJsonBytes);
                // Cache files are disposable. AtomicJsonFile's safety backup is useful during the
                // replace, but keeping it afterwards would silently double cache disk usage.
                TryDeleteFile(path + ".bak");
                PruneUnsafe(Math.Clamp(maximumEntries, 10, 5000), cancellationToken);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsExpectedCachePersistenceFailure(ex))
            {
                _log.Error("AiCacheWrite", ex);
                TryDeleteCachePath(path);
                return false;
            }
        }
        finally { _gate.Release(); }
    }

    private void PruneUnsafe(int maximumEntries, CancellationToken cancellationToken)
    {
        var newest = new PriorityQueue<FileInfo, long>();
        var scanned = 0;
        foreach (var path in Directory.EnumerateFiles(_paths.AiCacheRoot, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scanned++ >= MaximumFilesScannedPerPrune) break;

            FileInfo file;
            long priority;
            try
            {
                file = new FileInfo(path);
                priority = file.LastWriteTimeUtc.Ticks;
                if (file.Length <= 0 || file.Length > MaximumEntryJsonBytes)
                {
                    TryDeleteCachePath(path);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (newest.Count < maximumEntries)
            {
                newest.Enqueue(file, priority);
                continue;
            }

            if (newest.TryPeek(out var oldest, out var oldestPriority) && priority > oldestPriority)
            {
                newest.Dequeue();
                TryDeleteCachePath(oldest.FullName);
                newest.Enqueue(file, priority);
            }
            else
            {
                TryDeleteCachePath(file.FullName);
            }
        }

        CleanupAncillaryFilesUnsafe(cancellationToken);
    }

    private void CleanupAncillaryFilesUnsafe(CancellationToken cancellationToken)
    {
        var cleaned = 0;
        foreach (var path in Directory.EnumerateFiles(_paths.AiCacheRoot, "*.json.bak"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cleaned++ >= MaximumAncillaryFilesCleanedPerPrune) break;
            TryDeleteFile(path);
        }

        cleaned = 0;
        foreach (var path in Directory.EnumerateFiles(_paths.AiCacheRoot, "*.json.tmp-*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cleaned++ >= MaximumAncillaryFilesCleanedPerPrune) break;
            TryDeleteFile(path);
        }
    }

    private static void TryDeleteCachePath(string path)
    {
        TryDeleteFile(path);
        TryDeleteFile(path + ".bak");
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsExpectedCachePersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException;

    public async Task<AiCacheRepairReport> RepairAsync(TimeSpan maximumAge, int maximumEntries, CancellationToken cancellationToken = default)
    {
        if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        maximumEntries = Math.Clamp(maximumEntries, 10, 5000);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var scanned = 0;
            var deleted = 0;
            var invalid = 0;
            var expired = 0;
            var failed = 0;
            var keep = new List<(string Path, DateTimeOffset CreatedUtc)>();

            foreach (var path in Directory.EnumerateFiles(_paths.AiCacheRoot, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scanned++ >= MaximumFilesScannedPerPrune) break;
                try
                {
                    var info = new FileInfo(path);
                    var fileName = info.Name;
                    if (info.Length <= 0 || info.Length > MaximumEntryJsonBytes)
                    {
                        TryDeleteCachePath(path); deleted++; invalid++; continue;
                    }

                    var entry = await ReadEntryDirectAsync(path, info.Length, cancellationToken);
                    if (entry is null)
                    {
                        TryDeleteCachePath(path); deleted++; invalid++; continue;
                    }
                    var decision = AiCacheMaintenancePolicy.Decide(fileName, entry.Key, entry.CreatedUtc, now, info.Length, maximumAge);
                    if (decision == AiCacheMaintenanceDecision.Keep)
                    {
                        keep.Add((path, entry.CreatedUtc));
                        continue;
                    }

                    TryDeleteCachePath(path);
                    deleted++;
                    if (decision == AiCacheMaintenanceDecision.DeleteExpired) expired++; else invalid++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    failed++;
                    _log.Error("AiCacheRepair", ex);
                    TryDeleteCachePath(path);
                }
            }

            foreach (var extra in keep.OrderByDescending(item => item.CreatedUtc).Skip(maximumEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDeleteCachePath(extra.Path);
                deleted++;
            }
            var kept = Math.Min(keep.Count, maximumEntries);
            var ancillary = CleanupAncillaryFilesForRepairUnsafe(cancellationToken);
            return new AiCacheRepairReport(scanned, kept, deleted, invalid, expired, ancillary, failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<AiResultCacheEntry?> ReadEntryDirectAsync(string path, long expectedLength, CancellationToken cancellationToken)
    {
        if (expectedLength <= 0 || expectedLength > MaximumEntryJsonBytes) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedLength || stream.Length > MaximumEntryJsonBytes) return null;
        return await JsonSerializer.DeserializeAsync<AiResultCacheEntry>(stream, cancellationToken: cancellationToken);
    }

    private int CleanupAncillaryFilesForRepairUnsafe(CancellationToken cancellationToken)
    {
        var deleted = 0;
        var scanned = 0;
        foreach (var path in Directory.EnumerateFiles(_paths.AiCacheRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scanned++ >= MaximumAncillaryFilesCleanedPerPrune * 2) break;
            var name = Path.GetFileName(path);
            if (!name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains(".json.tmp-", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(path))
            {
                TryDeleteFile(path);
                if (!File.Exists(path)) deleted++;
            }
        }
        return deleted;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_paths.AiCacheRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(".json.tmp-", StringComparison.OrdinalIgnoreCase))
                    TryDeleteFile(file);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string PathFor(string key)
    {
        var safe = key.Length == 64 && key.All(Uri.IsHexDigit)
            ? key.ToLowerInvariant()
            : throw new ArgumentException("Invalid AI cache key.", nameof(key));
        return Path.Combine(_paths.AiCacheRoot, safe + ".json");
    }
}
