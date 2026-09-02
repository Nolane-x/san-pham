using Magic.Capture.App.Imaging;
using Magic.Capture.App.Views;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.App.Capture;

internal enum OverlayCaptureAction
{
    Result,
    Copy,
    Pin,
    Save,
    Text,
    Table,
    Barcode,
    Edit,
    Color,
    Magic,
    Workflow
}

internal sealed record CaptureRequestResult(
    IReadOnlyList<CaptureAsset> Assets,
    OverlayCaptureAction Action,
    string? WorkflowId,
    PixelRect SelectionBounds)
{
    public CaptureAsset Asset => Assets.Count > 0
        ? Assets[0]
        : throw new InvalidOperationException("Capture request result contains no assets.");
}

internal sealed class CaptureCoordinator
{
    private readonly MonitorService _monitors;
    private readonly ScreenCaptureService _screenCapture;
    private readonly WindowCaptureService _windowCapture;
    private readonly UiAutomationSnapshotService _uiAutomation;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    public LastRegionState? LastRegion { get; private set; }

    public CaptureCoordinator(MonitorService monitors, ScreenCaptureService screenCapture, WindowCaptureService windowCapture, UiAutomationSnapshotService uiAutomation)
    {
        _monitors = monitors;
        _screenCapture = screenCapture;
        _windowCapture = windowCapture;
        _uiAutomation = uiAutomation;
    }

    public async Task<CaptureRequestResult?> CaptureRegionAsync(OverlayCaptureAction defaultAction = OverlayCaptureAction.Result, bool includeCursor = false, ProductTier tier = ProductTier.Free, string? defaultWorkflowId = null, CaptureOverlayTheme overlayTheme = CaptureOverlayTheme.Dark, bool rectangularOnly = false, IReadOnlyList<PersonalizationActionItem>? actionLayout = null)
    {
        if (!await _captureGate.WaitAsync(0)) return null;
        try
        {
            var monitor = _monitors.GetActiveMonitor();
            var windowCatalog = _windowCapture.ListCapturableWindows();
            var uiAutomationTask = rectangularOnly
                ? Task.FromResult(UiAutomationSnapshot.Empty)
                : _uiAutomation.CaptureForMonitorAsync(monitor.Bounds, windowCatalog);
            var frozen = _screenCapture.Capture(
                monitor.Bounds, CaptureSourceKind.Monitor, monitor.DeviceName, includeCursor, monitorName: monitor.DeviceName,
                monitorHandle: monitor.Handle, sourceBounds: monitor.Bounds, targetKind: CaptureTargetKind.Monitor);
            var snapshot = await uiAutomationTask;
            var snapBounds = windowCatalog
                .Select(target => target.Bounds.Intersect(monitor.Bounds))
                .Where(bounds => !bounds.IsEmpty)
                .Select(bounds => new PixelRect(bounds.X - monitor.Bounds.X, bounds.Y - monitor.Bounds.Y, bounds.Width, bounds.Height))
                .Take(128)
                .ToArray();
            var uiAutomationTargets = UiAutomationSnapshotRules.ProjectSnapTargets(snapshot, monitor.Bounds);
            var windowTargets = windowCatalog
                .Where(target => !target.Bounds.Intersect(monitor.Bounds).IsEmpty)
                .OrderBy(target => target.ZOrder)
                .Take(128)
                .Select(target =>
                {
                    var clipped = target.Bounds.Intersect(monitor.Bounds);
                    var local = new PixelRect(
                        clipped.X - monitor.Bounds.X,
                        clipped.Y - monitor.Bounds.Y,
                        clipped.Width,
                        clipped.Height);
                    var label = string.IsNullOrWhiteSpace(target.Title) ? "Window" : $"Window · {target.Title}";
                    return new UiAutomationSnapTarget($"window:{target.ZOrder}:{target.Handle}", local, label, "Window", target.ZOrder, -1);
                });
            // Include top-level window fallbacks in the same z-ordered target set. If a UIA provider
            // for the foreground window fails, a control from a window behind it must never steal snap.
            var smartSnapTargets = windowTargets
                .Concat(uiAutomationTargets)
                .Take(UiAutomationSnapshotRules.MaximumSnapTargets)
                .ToArray();
            var overlay = new CaptureOverlayWindow(monitor, frozen.PngBytes, defaultAction, tier, defaultWorkflowId, overlayTheme, snapBounds, rectangularOnly, smartSnapTargets, actionLayout);
            var selection = await overlay.SelectAsync();
            if (selection is null || selection.Bounds.IsEmpty) return null;

            var globalBounds = _monitors.ToDesktopBounds(monitor, selection.Bounds);
            LastRegion = new LastRegionState(globalBounds, monitor.DeviceName, DateTimeOffset.UtcNow);

            IReadOnlyList<CaptureAsset> assets;
            if (selection.Geometry.Kind == CaptureSelectionKind.MultiRegion && selection.MultiRegionOutput == MultiRegionOutputMode.SeparateImages)
            {
                var regions = CaptureSelectionImageRenderer.RenderSeparateRegions(frozen.PngBytes, selection.Geometry);
                assets = regions.Select((region, index) =>
                {
                    var regionBounds = _monitors.ToDesktopBounds(monitor, region.Bounds);
                    var regionGeometry = CaptureSelectionGeometry.Rectangle(region.Bounds);
                    var uiAutomationNodes = UiAutomationSnapshotRules.ProjectForCapture(snapshot, monitor.Bounds, regionGeometry);
                    return CaptureAsset.Create(
                        regionBounds,
                        region.PngBytes,
                        CaptureSourceKind.Region,
                        $"{monitor.DeviceName} · region {index + 1}/{regions.Count}",
                        monitorName: monitor.DeviceName,
                        uiAutomationNodes: uiAutomationNodes);
                }).ToArray();
            }
            else
            {
                var rendered = CaptureSelectionImageRenderer.Render(frozen.PngBytes, selection.Geometry);
                var sourceName = selection.Geometry.Kind == CaptureSelectionKind.Rectangle
                    ? monitor.DeviceName
                    : $"{selection.Geometry.Kind} · {monitor.DeviceName}";
                var uiAutomationNodes = UiAutomationSnapshotRules.ProjectForCapture(snapshot, monitor.Bounds, selection.Geometry);
                assets = [CaptureAsset.Create(globalBounds, rendered, CaptureSourceKind.Region, sourceName, monitorName: monitor.DeviceName, uiAutomationNodes: uiAutomationNodes)];
            }

            return new CaptureRequestResult(assets, selection.Action, selection.WorkflowId, globalBounds);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public CaptureAsset CaptureExactRegion(PixelRect requestedBounds, bool includeCursor = false, string? sourceName = null)
    {
        var virtualBounds = _monitors.GetVirtualScreenBounds();
        var bounds = CaptureRegionRules.Normalize(requestedBounds, virtualBounds);
        if (bounds.IsEmpty) throw new InvalidOperationException("The requested capture region is outside the current desktop.");
        LastRegion = new LastRegionState(bounds, null, DateTimeOffset.UtcNow);
        return _screenCapture.Capture(bounds, CaptureSourceKind.Region, sourceName ?? "Exact Region", includeCursor);
    }

    public CaptureAsset CaptureLastRegion(bool includeCursor = false)
    {
        var last = LastRegion ?? throw new InvalidOperationException("No previous region is available yet.");
        var virtualBounds = _monitors.GetVirtualScreenBounds();
        if (!last.IsUsableWithin(virtualBounds))
            throw new InvalidOperationException("The previous capture region is no longer on the current desktop layout.");
        var bounds = last.GlobalBounds.Intersect(virtualBounds);
        return _screenCapture.Capture(bounds, CaptureSourceKind.Region, "Repeated Region", includeCursor, monitorName: last.MonitorDeviceName);
    }

    public CaptureAsset CaptureActiveMonitor(bool includeCursor = false)
    {
        var monitor = _monitors.GetActiveMonitor();
        return _screenCapture.Capture(
            monitor.Bounds, CaptureSourceKind.Monitor, monitor.DeviceName, includeCursor, monitorName: monitor.DeviceName,
            monitorHandle: monitor.Handle, sourceBounds: monitor.Bounds, targetKind: CaptureTargetKind.Monitor);
    }

    public CaptureAsset CaptureMonitor(MonitorInfo monitor, bool includeCursor = false)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var visible = monitor.Bounds.Intersect(_monitors.GetVirtualScreenBounds());
        if (visible.IsEmpty) throw new InvalidOperationException("The selected monitor is no longer available.");
        return _screenCapture.Capture(
            visible, CaptureSourceKind.Monitor, monitor.DeviceName, includeCursor, monitorName: monitor.DeviceName,
            monitorHandle: monitor.Handle, sourceBounds: monitor.Bounds, targetKind: CaptureTargetKind.Monitor);
    }

    public CaptureAsset CaptureVirtualDesktop(bool includeCursor = false)
    {
        var bounds = _monitors.GetVirtualScreenBounds();
        return _screenCapture.Capture(bounds, CaptureSourceKind.VirtualDesktop, "Virtual Desktop", includeCursor);
    }
}
