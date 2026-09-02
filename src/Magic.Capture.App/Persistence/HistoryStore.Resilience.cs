using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.History;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.Persistence;

internal sealed record HistoryRepairResult(
    HistoryMaintenancePlan Before,
    HistoryMaintenancePlan After,
    int RecoveredPrimaryCount,
    int RemovedMissingRows,
    int RebuiltThumbnails,
    int RebuiltFingerprints,
    int RemovedOrphanThumbnails,
    int FailureCount);

internal sealed partial class HistoryStore
{
    private const int MaximumMaintenanceFiles = 200_000;
    private HistoryTextIndex? _textIndex;

    public async Task<IReadOnlyList<HistoryItem>> SearchAsync(
        string? query,
        HistoryQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadIndexUnsafeAsync(cancellationToken);
            _textIndex ??= HistoryTextIndex.Build(items);
            var candidateIds = _textIndex.Search(query).ToHashSet();
            var matches = items.Where(item => candidateIds.Contains(item.Id) && HistorySearch.Matches(item, query));
            var library = _library is null ? null : await _library.LoadAsync(cancellationToken);
            return HistoryQuery.Apply(matches, options, library);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryItem?> ImportPortableAsync(HistoryItem source, byte[] pngBytes, AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (!PngDimensions.TryRead(pngBytes, out var width, out var height))
            throw new InvalidDataException("History archive image is not a valid PNG.");
        ImageWorkloadLimits.ValidateDimensions(width, height);
        ImageWorkloadLimits.ValidateEncodedLength(pngBytes.LongLength);
        if (source.Width != width || source.Height != height)
            throw new InvalidDataException("History archive metadata dimensions do not match its PNG payload.");

        var kind = Enum.TryParse<CaptureSourceKind>(source.SourceKind, ignoreCase: true, out var parsed) ? parsed : CaptureSourceKind.Imported;
        var createdUtc = source.CreatedUtc > DateTimeOffset.UtcNow.AddDays(1) ? DateTimeOffset.UtcNow : source.CreatedUtc;
        var asset = new CaptureAsset(
            Guid.NewGuid(),
            createdUtc,
            new Magic.Capture.Core.Geometry.PixelRect(0, 0, width, height),
            pngBytes,
            width,
            height,
            kind,
            source.SourceDisplayName ?? "Imported history archive",
            source.WindowTitle,
            source.ProcessName,
            source.MonitorName,
            ExecutablePath: source.ExecutablePath);
        var imported = await AddAsync(asset, settings with { HistoryEnabled = true }, source.OcrPreview, source.BarcodePreview, cancellationToken);
        if (imported is null) return null;
        var metadata = HistoryMetadata.Normalize(source.Title, source.Notes, source.Tags, source.IsFavorite);
        await UpdateMetadataAsync(imported.Id, metadata, cancellationToken);
        return imported with { Title = metadata.Title, Notes = metadata.Notes, Tags = metadata.Tags.ToArray(), IsFavorite = metadata.IsFavorite ?? imported.IsFavorite };
    }

    public async Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _textIndex = HistoryTextIndex.Build(await LoadIndexUnsafeAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistorySessionSummary>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return HistorySessions.Summarize(await LoadIndexUnsafeAsync(cancellationToken)); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<HistoryDuplicateGroup>> GetExactDuplicateGroupsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return HistoryDuplicateIndex.FindExact(await LoadIndexUnsafeAsync(cancellationToken)); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<HistoryDuplicateGroup>> GetNearDuplicateGroupsAsync(int maximumHammingDistance = 6, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return HistoryDuplicateIndex.FindNear(await LoadIndexUnsafeAsync(cancellationToken), maximumHammingDistance); }
        finally { _gate.Release(); }
    }

    public async Task<HistoryMaintenancePlan> ScanHealthAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadIndexUnsafeAsync(cancellationToken);
            return ScanHealthUnsafe(items, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryRepairResult> RepairAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadIndexUnsafeAsync(cancellationToken);
            var before = ScanHealthUnsafe(items, cancellationToken);
            var failures = 0;
            var removedRows = 0;
            var recovered = 0;
            var rebuiltThumbnails = 0;
            var rebuiltFingerprints = 0;
            var removedOrphanThumbnails = 0;

            if (before.RowsWithoutPrimary.Count > 0)
            {
                var missing = before.RowsWithoutPrimary.ToHashSet();
                removedRows = items.RemoveAll(item => missing.Contains(item.Id));
            }

            foreach (var relative in before.OrphanPrimaryPaths.Take(MaximumMaintenanceFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var recoveredItem = await TryRecoverItemUnsafeAsync(relative, cancellationToken);
                    if (recoveredItem is null || items.Any(item => item.Id == recoveredItem.Id)) continue;
                    items.Add(recoveredItem);
                    recovered++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failures++;
                }
            }

            var missingThumbIds = before.MissingThumbnailItemIds.ToHashSet();
            var missingFingerprintIds = before.MissingFingerprintItemIds.ToHashSet();
            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[index];
                var needsThumb = missingThumbIds.Contains(item.Id) ||
                    (HistoryThumbnailPolicy.ShouldPreGenerate(item.Width, item.Height) && string.IsNullOrWhiteSpace(item.ThumbnailRelativePath));
                var needsFingerprint = missingFingerprintIds.Contains(item.Id) ||
                    !HistoryDuplicateIndex.IsSha256(item.ContentSha256) || !item.PerceptualHash64.HasValue;
                if (!needsThumb && !needsFingerprint) continue;

                try
                {
                    var primary = GetAbsolutePath(item);
                    if (!File.Exists(primary)) continue;
                    var bytes = await ImageFileReader.ReadAsync(primary, cancellationToken);
                    if (needsFingerprint)
                    {
                        var fingerprints = ImageFingerprintService.Compute(bytes);
                        item = item with { ContentSha256 = fingerprints.Sha256, PerceptualHash64 = fingerprints.DHash64 };
                        rebuiltFingerprints++;
                    }

                    if (needsThumb && HistoryThumbnailPolicy.ShouldPreGenerate(item.Width, item.Height))
                    {
                        var relativeThumb = HistoryMaintenance.ExpectedThumbnailPath(item);
                        var absoluteThumb = LocalPathGuard.ResolveWithinRoot(_paths.HistoryRoot, relativeThumb);
                        var thumbnail = CreateThumbnail(bytes, 256, 160);
                        await AtomicFile.WriteBytesAsync(absoluteThumb, thumbnail, cancellationToken);
                        item = item with { ThumbnailRelativePath = relativeThumb };
                        rebuiltThumbnails++;
                    }
                    items[index] = item;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Runtime.InteropServices.ExternalException)
                {
                    failures++;
                }
            }

            foreach (var relative in before.OrphanThumbnailPaths.Take(MaximumMaintenanceFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var path = LocalPathGuard.ResolveWithinRoot(_paths.HistoryRoot, relative);
                    if (File.Exists(path)) File.Delete(path);
                    removedOrphanThumbnails++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failures++;
                }
            }

            await SaveIndexUnsafeAsync(items, cancellationToken);
            await ApplyRetentionUnsafeAsync(items, settings, cancellationToken);
            _textIndex = HistoryTextIndex.Build(items);
            var after = ScanHealthUnsafe(items, cancellationToken);
            return new HistoryRepairResult(before, after, recovered, removedRows, rebuiltThumbnails, rebuiltFingerprints, removedOrphanThumbnails, failures);
        }
        finally
        {
            _gate.Release();
        }
    }

    private HistoryMaintenancePlan ScanHealthUnsafe(IReadOnlyList<HistoryItem> items, CancellationToken cancellationToken)
    {
        var primaryPaths = new List<string>();
        var thumbnailPaths = new List<string>();
        if (Directory.Exists(_paths.HistoryRoot))
        {
            var scanned = 0;
            foreach (var path in Directory.EnumerateFiles(_paths.HistoryRoot, "*.png", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scanned++ >= MaximumMaintenanceFiles) break;
                var relative = Path.GetRelativePath(_paths.HistoryRoot, path);
                if (path.EndsWith(".thumb.png", StringComparison.OrdinalIgnoreCase)) thumbnailPaths.Add(relative);
                else primaryPaths.Add(relative);
            }
        }
        return HistoryMaintenance.Plan(items, primaryPaths, thumbnailPaths);
    }

    private async Task<HistoryItem?> TryRecoverItemUnsafeAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!LocalPathGuard.IsWithinRoot(_paths.HistoryRoot, relativePath)) return null;
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (!Guid.TryParseExact(fileName, "N", out var id)) return null;
        if (!HistoryStoragePathPolicy.IsExpectedPrimary(id, relativePath)) return null;
        var path = LocalPathGuard.ResolveWithinRoot(_paths.HistoryRoot, relativePath);
        if (!File.Exists(path)) return null;

        var header = new byte[24];
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var offset = 0;
            while (offset < header.Length)
            {
                var read = await stream.ReadAsync(header.AsMemory(offset), cancellationToken);
                if (read == 0) return null;
                offset += read;
            }
        }
        if (!PngDimensions.TryRead(header, out var width, out var height)) return null;
        ImageWorkloadLimits.ValidateDimensions(width, height);
        var info = new FileInfo(path);
        ImageWorkloadLimits.ValidateEncodedLength(info.Length);
        var item = new HistoryItem(
            id,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            relativePath,
            width,
            height,
            "Recovered",
            null,
            null,
            info.Length,
            null,
            SessionId: HistoryMetadata.NormalizeSessionId("recovered"),
            SourceDisplayName: "Recovered history");
        return item;
    }
}
