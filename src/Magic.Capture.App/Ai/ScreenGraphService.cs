using Magic.Capture.App.Analysis;
using Magic.Capture.App.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Utilities;

namespace Magic.Capture.App.Ai;

internal sealed class ScreenGraphService
{
    private const int MaxCachedGraphs = 64;
    private readonly AnalysisService _analysis;
    private readonly object _cacheGate = new();
    private readonly Dictionary<ScreenGraphCacheKey, CacheEntry> _cache = [];
    private readonly LinkedList<ScreenGraphCacheKey> _lru = [];

    public ScreenGraphService(AnalysisService analysis) => _analysis = analysis;

    public async Task<ScreenGraphDocument> BuildAsync(CaptureAsset asset, AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new ScreenGraphCacheKey(
            asset.Id,
            HashUtility.ComputeSha256(asset.PngBytes),
            settings.PreferredOcrLanguage?.Trim() ?? string.Empty);

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                _lru.Remove(cached.Node);
                _lru.AddLast(cached.Node);
                return cached.Document;
            }
        }

        // Cache only completed documents. A caller cancellation can stop this build, but can never
        // leave a cancelled/faulted Task stored for future callers of the same capture.
        var document = await BuildCoreAsync(asset, settings, cancellationToken);

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing.Node);
                _lru.AddLast(existing.Node);
                return existing.Document;
            }

            var node = _lru.AddLast(key);
            _cache[key] = new CacheEntry(document, node);
            while (_cache.Count > MaxCachedGraphs && _lru.First is { } oldest)
            {
                _lru.RemoveFirst();
                _cache.Remove(oldest.Value);
            }
        }

        return document;
    }

    public void Forget(Guid captureId)
    {
        lock (_cacheGate)
        {
            foreach (var key in _cache.Keys.Where(key => key.CaptureId == captureId).ToArray())
            {
                if (!_cache.Remove(key, out var entry)) continue;
                _lru.Remove(entry.Node);
            }
        }
    }

    private async Task<ScreenGraphDocument> BuildCoreAsync(CaptureAsset asset, AppSettings settings, CancellationToken cancellationToken)
    {
        var analysis = await _analysis.AnalyzeAsync(asset, settings, cancellationToken);
        var barcodes = analysis.Barcodes.Select(hit =>
            new ScreenBarcode(hit.Format, hit.Text, hit.Bounds ?? new PixelRect(0, 0, asset.Width, asset.Height))).ToArray();

        return ScreenGraphBuilder.Build(new ScreenGraphBuildInput(
            asset.Id,
            asset.CreatedUtc,
            asset.SourceKind.ToString(),
            asset.SourceDisplayName,
            asset.Width,
            asset.Height,
            new PixelRect(0, 0, asset.Width, asset.Height),
            analysis.Ocr,
            analysis.Table,
            barcodes,
            asset.UiAutomationNodes));
    }

    private readonly record struct ScreenGraphCacheKey(Guid CaptureId, string ImageHash, string OcrLanguage);
    private sealed record CacheEntry(ScreenGraphDocument Document, LinkedListNode<ScreenGraphCacheKey> Node);
}
