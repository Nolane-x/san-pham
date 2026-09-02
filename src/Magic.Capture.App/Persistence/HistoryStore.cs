using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using System.Drawing;
using Magic.Capture.Core.History;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.Persistence;

internal sealed partial class HistoryStore
{
    private const long MaximumHistoryIndexJsonBytes = 128L * 1024 * 1024;
    private readonly AppPaths _paths;
    private readonly HistoryLibraryStore? _library;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _sessionId = HistoryMetadata.NormalizeSessionId($"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}")!;

    public HistoryStore(AppPaths paths, HistoryLibraryStore? library = null) { _paths = paths; _library = library; }

    public async Task<HistoryItem?> AddAsync(
        CaptureAsset asset,
        AppSettings settings,
        string? ocrPreview = null,
        string? barcodePreview = null,
        CancellationToken cancellationToken = default)
    {
        if (!settings.HistoryEnabled) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Recover any interrupted add from the previous process/session before opening a new
            // single-writer transaction. HistoryStore's gate guarantees at most one pending add.
            var items = await LoadIndexUnsafeAsync(cancellationToken);

            var date = asset.CreatedUtc.ToLocalTime();
            var folder = Path.Combine(_paths.HistoryRoot, date.ToString("yyyy"), date.ToString("MM"), date.ToString("dd"));
            Directory.CreateDirectory(folder);
            var fileName = asset.Id.ToString("N") + ".png";
            var absolutePath = Path.Combine(folder, fileName);
            var relative = Path.GetRelativePath(_paths.HistoryRoot, absolutePath);
            var thumbnailPath = Path.Combine(folder, asset.Id.ToString("N") + ".thumb.png");
            byte[]? thumbnailBytes = null;
            string? thumbnailRelative = null;
            if (HistoryThumbnailPolicy.ShouldPreGenerate(asset.Width, asset.Height))
            {
                try
                {
                    thumbnailBytes = CreateThumbnail(asset.PngBytes, 256, 160);
                    thumbnailRelative = Path.GetRelativePath(_paths.HistoryRoot, thumbnailPath);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Runtime.InteropServices.ExternalException)
                {
                    // A thumbnail is an optimization only. Never reject a valid capture because the
                    // preview decoder failed; the History UI can decode the original at preview size.
                    thumbnailBytes = null;
                    thumbnailRelative = null;
                }
            }
            var item = new HistoryItem(
                asset.Id,
                asset.CreatedUtc,
                relative,
                asset.Width,
                asset.Height,
                asset.SourceKind.ToString(),
                Truncate(ocrPreview, 240),
                Truncate(barcodePreview, 240),
                asset.PngBytes.LongLength,
                thumbnailRelative,
                SessionId: _sessionId,
                SourceDisplayName: Truncate(asset.SourceDisplayName, 512),
                WindowTitle: Truncate(asset.WindowTitle, 512),
                ProcessName: Truncate(asset.ProcessName, 260),
                MonitorName: Truncate(asset.MonitorName, 260),
                ExecutablePath: Truncate(asset.ExecutablePath, 2048),
                ContentSha256: ImageFingerprintService.ComputeSha256(asset.PngBytes),
                PerceptualHash64: TryComputeDHash(asset.PngBytes));

            // Journal the intended index row before creating final files. If the process dies at
            // any later point, LoadIndexUnsafeAsync can deterministically complete or discard it.
            await AtomicJsonFile.WriteAsync(_paths.HistoryPendingAddFile, item, cancellationToken);
            await AtomicFile.WriteBytesAsync(absolutePath, asset.PngBytes, cancellationToken);
            if (thumbnailBytes is not null)
            {
                try
                {
                    await AtomicFile.WriteBytesAsync(thumbnailPath, thumbnailBytes, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The primary capture is already durable. Missing thumbnails are recoverable and
                    // must not turn a successful capture into a failed History transaction.
                }
            }

            items.RemoveAll(existing => existing.Id == item.Id);
            items.Add(item);
            await SaveIndexUnsafeAsync(items, cancellationToken);
            DeletePendingAddJournal();
            await ApplyRetentionUnsafeAsync(items, settings, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadIndexUnsafeAsync(cancellationToken)).OrderByDescending(x => x.CreatedUtc).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public string GetAbsolutePath(HistoryItem item) => LocalPathGuard.ResolveWithinRoot(_paths.HistoryRoot, item.RelativePath);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            var item = items.FirstOrDefault(x => x.Id == id);
            if (item is null) return;
            if (!TryDeletePrimaryFile(item)) return;
            TryDeleteThumbnail(item);
            items.RemoveAll(x => x.Id == id);
            await SaveIndexUnsafeAsync(items, cancellationToken);
            if (_library is not null) await _library.PruneAssetsBestEffortAsync([id], CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<Guid>? ids, CancellationToken cancellationToken = default)
    {
        var selected = (ids ?? []).Distinct().Take(5_000).ToHashSet();
        if (selected.Count == 0) return 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            var removed = 0;
            var removedIds = new HashSet<Guid>();
            foreach (var item in items.Where(item => selected.Contains(item.Id)).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryDeletePrimaryFile(item)) continue;
                TryDeleteThumbnail(item);
                removedIds.Add(item.Id);
            }
            if (removedIds.Count == 0) return 0;
            items.RemoveAll(item => removedIds.Contains(item.Id));
            await SaveIndexUnsafeAsync(items, cancellationToken);
            if (_library is not null) await _library.PruneAssetsBestEffortAsync(removedIds, CancellationToken.None);
            return removedIds.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> AddTagsAsync(IEnumerable<Guid>? ids, IEnumerable<string>? tags, CancellationToken cancellationToken = default)
    {
        var selected = (ids ?? []).Distinct().Take(5_000).ToHashSet();
        var incoming = HistoryMetadata.Normalize(null, null, tags).Tags;
        if (selected.Count == 0 || incoming.Count == 0) return 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            var changed = 0;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (!selected.Contains(item.Id)) continue;
                var merged = HistoryMetadata.Normalize(item.Title, item.Notes, (item.Tags ?? []).Concat(incoming), item.IsFavorite);
                if (SequenceEqualIgnoreCase(item.Tags, merged.Tags)) continue;
                items[index] = item with { Tags = merged.Tags.ToArray() };
                changed++;
            }
            if (changed > 0) await SaveIndexUnsafeAsync(items, cancellationToken);
            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            var removedIds = new HashSet<Guid>();
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryDeletePrimaryFile(item)) continue;
                TryDeleteThumbnail(item);
                removedIds.Add(item.Id);
            }
            if (removedIds.Count == 0) return;
            items.RemoveAll(item => removedIds.Contains(item.Id));
            await SaveIndexUnsafeAsync(items, cancellationToken);
            if (_library is not null) await _library.PruneAssetsBestEffortAsync(removedIds, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyRetentionAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            await ApplyRetentionUnsafeAsync(items, settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyRetentionUnsafeAsync(List<HistoryItem> items, AppSettings settings, CancellationToken cancellationToken)
    {
        var policy = new HistoryRetentionPolicy(settings.HistoryMaximumAgeDays, settings.HistoryMaximumCount, settings.HistoryMaximumBytes);
        var deletions = HistoryRetentionPlanner.SelectForDeletion(items, policy, DateTimeOffset.UtcNow);
        if (deletions.Count == 0) return;
        var removedIds = new HashSet<Guid>();
        foreach (var item in items.Where(x => deletions.Contains(x.Id)).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryDeletePrimaryFile(item)) continue;
            TryDeleteThumbnail(item);
            removedIds.Add(item.Id);
        }
        if (removedIds.Count == 0) return;
        items.RemoveAll(x => removedIds.Contains(x.Id));
        await SaveIndexUnsafeAsync(items, cancellationToken);
        if (_library is not null) await _library.PruneAssetsBestEffortAsync(removedIds, CancellationToken.None);
    }

    private async Task<List<HistoryItem>> LoadIndexUnsafeAsync(CancellationToken cancellationToken)
    {
        List<HistoryItem> items;
        if (!File.Exists(_paths.HistoryIndexFile) && !File.Exists(_paths.HistoryIndexFile + ".bak"))
        {
            items = await RebuildIndexFromFilesUnsafeAsync(cancellationToken);
        }
        else
        {
            try
            {
                var loaded = await AtomicJsonFile.ReadAsync<List<HistoryItem>>(_paths.HistoryIndexFile, cancellationToken, MaximumHistoryIndexJsonBytes) ?? [];
                items = loaded
                    .Where(IsSafeHistoryItem)
                    .Select(NormalizeLoadedHistoryItem)
                    .GroupBy(item => item.Id)
                    .Select(group => group.OrderByDescending(item => item.CreatedUtc).First())
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                QuarantineCorruptIndex();
                items = await RebuildIndexFromFilesUnsafeAsync(cancellationToken);
                if (items.Count > 0) await SaveIndexUnsafeAsync(items, cancellationToken);
            }
        }

        await RecoverPendingAddUnsafeAsync(items, cancellationToken);
        return items;
    }

    private async Task RecoverPendingAddUnsafeAsync(List<HistoryItem> items, CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.HistoryPendingAddFile)) return;
        HistoryItem? pending;
        try
        {
            pending = await AtomicJsonFile.ReadAsync<HistoryItem>(_paths.HistoryPendingAddFile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            QuarantinePendingAddJournal();
            return;
        }

        if (pending is null || !IsSafeHistoryItem(pending))
        {
            QuarantinePendingAddJournal();
            return;
        }

        var primary = GetAbsolutePath(pending);
        if (!File.Exists(primary))
        {
            TryDeleteThumbnail(pending);
            DeletePendingAddJournal();
            return;
        }

        // Primary image is durable. Ensure the index contains exactly one row for it. A missing
        // thumbnail is acceptable; it can be regenerated later without losing the capture.
        items.RemoveAll(item => item.Id == pending.Id);
        items.Add(pending);
        await SaveIndexUnsafeAsync(items, cancellationToken);
        DeletePendingAddJournal();
    }

    private void DeletePendingAddJournal()
    {
        TryDeleteFile(_paths.HistoryPendingAddFile);
        TryDeleteFile(_paths.HistoryPendingAddFile + ".bak");
    }

    private void QuarantinePendingAddJournal()
    {
        if (!File.Exists(_paths.HistoryPendingAddFile)) return;
        var quarantine = Path.Combine(_paths.HistoryRoot, $"pending-add.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        try { File.Move(_paths.HistoryPendingAddFile, quarantine, overwrite: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        TryDeleteFile(_paths.HistoryPendingAddFile + ".bak");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private bool IsSafeHistoryItem(HistoryItem item)
    {
        if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.RelativePath)) return false;
        if (!HistoryStoragePathPolicy.IsExpectedPrimary(item.Id, item.RelativePath)) return false;
        if (!LocalPathGuard.IsWithinRoot(_paths.HistoryRoot, item.RelativePath)) return false;
        if (!string.IsNullOrWhiteSpace(item.ThumbnailRelativePath))
        {
            if (!HistoryStoragePathPolicy.IsExpectedThumbnail(item.Id, item.ThumbnailRelativePath)) return false;
            if (!LocalPathGuard.IsWithinRoot(_paths.HistoryRoot, item.ThumbnailRelativePath)) return false;
        }
        if (item.FileBytes < 0 || item.FileBytes > ImageWorkloadLimits.MaximumEncodedBytes) return false;
        try { ImageWorkloadLimits.ValidateDimensions(item.Width, item.Height); }
        catch (InvalidDataException) { return false; }
        catch (OverflowException) { return false; }
        return true;
    }

    private static HistoryItem NormalizeLoadedHistoryItem(HistoryItem item)
    {
        var metadata = HistoryMetadata.Normalize(item.Title, item.Notes, item.Tags, item.IsFavorite);
        return item with
        {
            SourceKind = Truncate(item.SourceKind, 64) ?? "Unknown",
            OcrPreview = Truncate(item.OcrPreview, 240),
            BarcodePreview = Truncate(item.BarcodePreview, 240),
            Title = metadata.Title,
            Notes = metadata.Notes,
            Tags = metadata.Tags.ToArray(),
            SessionId = HistoryMetadata.NormalizeSessionId(item.SessionId),
            SourceDisplayName = Truncate(item.SourceDisplayName, 512),
            WindowTitle = Truncate(item.WindowTitle, 512),
            ProcessName = Truncate(item.ProcessName, 260),
            MonitorName = Truncate(item.MonitorName, 260),
            ExecutablePath = Truncate(item.ExecutablePath, 2048),
            ContentSha256 = HistoryDuplicateIndex.IsSha256(item.ContentSha256) ? item.ContentSha256!.ToLowerInvariant() : null
        };
    }

    private void QuarantineCorruptIndex()
    {
        if (!File.Exists(_paths.HistoryIndexFile)) return;
        var quarantine = Path.Combine(_paths.HistoryRoot, $"index.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        try { File.Move(_paths.HistoryIndexFile, quarantine, overwrite: false); }
        catch (IOException) { /* Keep the corrupt file untouched if it cannot be quarantined. */ }
        catch (UnauthorizedAccessException) { /* Keep the corrupt file untouched if it cannot be quarantined. */ }
    }

    private async Task<List<HistoryItem>> RebuildIndexFromFilesUnsafeAsync(CancellationToken cancellationToken)
    {
        var recovered = new List<HistoryItem>();
        if (!Directory.Exists(_paths.HistoryRoot)) return recovered;
        foreach (var path in Directory.EnumerateFiles(_paths.HistoryRoot, "*.png", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.EndsWith(".thumb.png", StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(fileName, "N", out var id)) continue;
            try
            {
                var header = new byte[24];
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var read = await stream.ReadAsync(header, cancellationToken);
                if (read < header.Length || !Magic.Capture.Core.Capture.PngDimensions.TryRead(header, out var width, out var height)) continue;
                var info = new FileInfo(path);
                var thumbnail = Path.Combine(Path.GetDirectoryName(path)!, id.ToString("N") + ".thumb.png");
                recovered.Add(new HistoryItem(
                    id, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), Path.GetRelativePath(_paths.HistoryRoot, path),
                    width, height, "Recovered", null, null, info.Length,
                    File.Exists(thumbnail) ? Path.GetRelativePath(_paths.HistoryRoot, thumbnail) : null,
                    SourceDisplayName: "Recovered history"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return recovered.OrderBy(item => item.CreatedUtc).ToList();
    }

    private async Task SaveIndexUnsafeAsync(IReadOnlyList<HistoryItem> items, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(_paths.HistoryIndexFile, items, cancellationToken, MaximumHistoryIndexJsonBytes);
        _textIndex = null;
    }

    public string? GetThumbnailAbsolutePath(HistoryItem item) =>
        string.IsNullOrWhiteSpace(item.ThumbnailRelativePath) ? null : LocalPathGuard.ResolveWithinRoot(_paths.HistoryRoot, item.ThumbnailRelativePath);

    public async Task UpdatePreviewsAsync(Guid id, string? ocrPreview, string? barcodePreview, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0) return;
            items[index] = items[index] with { OcrPreview = Truncate(ocrPreview, 240), BarcodePreview = Truncate(barcodePreview, 240) };
            await SaveIndexUnsafeAsync(items, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateMetadataAsync(Guid id, HistoryMetadataUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        update = HistoryMetadata.Normalize(update.Title, update.Notes, update.Tags, update.IsFavorite);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadIndexUnsafeAsync(cancellationToken)).ToList();
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0) return;
            var item = items[index];
            items[index] = item with
            {
                Title = update.Title,
                Notes = update.Notes,
                Tags = update.Tags.ToArray(),
                IsFavorite = update.IsFavorite ?? item.IsFavorite
            };
            await SaveIndexUnsafeAsync(items, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryDeletePrimaryFile(HistoryItem item)
    {
        try
        {
            var path = GetAbsolutePath(item);
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void TryDeleteThumbnail(HistoryItem item)
    {
        try { DeleteThumbnail(item); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void DeleteThumbnail(HistoryItem item)
    {
        var path = GetThumbnailAbsolutePath(item);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }

    private static bool SequenceEqualIgnoreCase(IReadOnlyList<string>? left, IReadOnlyList<string> right)
    {
        if ((left?.Count ?? 0) != right.Count) return false;
        if (left is null) return right.Count == 0;
        for (var index = 0; index < left.Count; index++)
            if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static ulong? TryComputeDHash(byte[] pngBytes)
    {
        try { return ImageFingerprintService.ComputeDifferenceHash64(pngBytes); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Runtime.InteropServices.ExternalException) { return null; }
    }

    private static byte[] CreateThumbnail(byte[] pngBytes, int maxWidth, int maxHeight)
    {
        using var source = BitmapCodec.Decode(pngBytes);
        var scale = Math.Min(1d, Math.Min(maxWidth / (double)source.Width, maxHeight / (double)source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var thumb = new Bitmap(source, new Size(width, height));
        return BitmapCodec.EncodePng(thumb);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
