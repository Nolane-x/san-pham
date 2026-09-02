using Magic.Capture.App.Ai;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Privacy;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.App.Privacy;

internal sealed record CaptureRedactionResult(CaptureAsset Asset, int FindingCount, int LayerCount);

internal sealed class CaptureRedactionService
{
    private readonly ScreenGraphService _screenGraph;
    private readonly AnnotationRenderer _renderer;

    public CaptureRedactionService(ScreenGraphService screenGraph, AnnotationRenderer renderer)
    {
        _screenGraph = screenGraph;
        _renderer = renderer;
    }

    public async Task<CaptureRedactionResult> RedactAsync(
        CaptureAsset asset,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var graph = await _screenGraph.BuildAsync(asset, settings, cancellationToken);
        var findings = SensitiveDataDetector.Scan(graph, new SensitiveDataOptions(settings.SensitivePatterns, settings.SensitiveWords));
        var plan = RedactionPlanner.Create(
            findings,
            new PixelRect(0, 0, asset.Width, asset.Height),
            settings.OutboundRedactionStyle,
            padding: 4);
        if (plan.Layers.Count == 0) return new CaptureRedactionResult(asset, findings.Count, 0);

        var png = _renderer.Render(asset.PngBytes, new AnnotationDocument(plan.Layers));
        return new CaptureRedactionResult(asset.WithPng(png), findings.Count, plan.Layers.Count);
    }
}
