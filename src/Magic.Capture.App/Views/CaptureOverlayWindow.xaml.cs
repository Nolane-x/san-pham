using Magic.Capture.App.Capture;
using Magic.Capture.App.Platform;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;

namespace Magic.Capture.App.Views;

internal sealed record OverlaySelection(CaptureSelectionGeometry Geometry, OverlayCaptureAction Action, string? WorkflowId = null, MultiRegionOutputMode MultiRegionOutput = MultiRegionOutputMode.Canvas)
{
    public PixelRect Bounds => Geometry.Bounds;
}

public sealed partial class CaptureOverlayWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly byte[] _frozenPng;
    private readonly TaskCompletionSource<OverlaySelection?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly OverlayCaptureAction _defaultAction;
    private readonly ProductTier _tier;
    private readonly string? _defaultWorkflowId;
    private readonly IReadOnlyList<PixelRect> _windowSnapBounds;
    private readonly IReadOnlyList<UiAutomationSnapTarget> _controlSnapTargets;
    private readonly IReadOnlyList<PixelRect> _edgeSnapBounds;
    private readonly bool _rectangularOnly;
    private readonly CompositeTransform _loupeTransform;
    private readonly List<PixelPoint> _pathPoints = [];
    private readonly List<PixelRect> _multiRegions = [];
    private readonly List<Microsoft.UI.Xaml.Shapes.Rectangle> _multiRegionVisuals = [];

    private Point _start;
    private Point _end;
    private bool _dragging;
    private bool _completed;
    private double? _aspectRatio;
    private bool _hasSelection;
    private bool _resizing;
    private bool _lightOverlay;
    private SelectionResizeHandle _resizeHandle;
    private PixelRect _resizeBasePhysical = PixelRect.Empty;
    private bool _snapEnabled = true;
    private bool _snapUserPreference = true;
    private PixelRect _hoverSnapBounds = PixelRect.Empty;
    private CaptureSelectionKind _selectionKind = CaptureSelectionKind.Rectangle;
    private PixelPoint? _pathHoverPoint;
    private bool _pathFinalized;
    private MultiRegionOutputMode _multiRegionOutput = MultiRegionOutputMode.Canvas;

    internal CaptureOverlayWindow(
        MonitorInfo monitor,
        byte[] frozenPng,
        OverlayCaptureAction defaultAction,
        ProductTier tier,
        string? defaultWorkflowId = null,
        CaptureOverlayTheme overlayTheme = CaptureOverlayTheme.Dark,
        IReadOnlyList<PixelRect>? windowSnapBounds = null,
        bool rectangularOnly = false,
        IReadOnlyList<UiAutomationSnapTarget>? controlSnapTargets = null,
        IReadOnlyList<PersonalizationActionItem>? actionLayout = null)
    {
        InitializeComponent();
        _monitor = monitor;
        _frozenPng = frozenPng;
        _defaultAction = defaultAction;
        _tier = tier;
        _defaultWorkflowId = defaultWorkflowId;
        _lightOverlay = overlayTheme == CaptureOverlayTheme.Light;
        _windowSnapBounds = (windowSnapBounds ?? []).Where(bounds => !bounds.IsEmpty).Take(128).ToArray();
        _controlSnapTargets = (controlSnapTargets ?? []).Where(target => !target.Bounds.IsEmpty).Take(UiAutomationSnapshotRules.MaximumSnapTargets).ToArray();
        _edgeSnapBounds = _windowSnapBounds
            .Concat(_controlSnapTargets.Select(target => target.Bounds))
            .Where(bounds => !bounds.IsEmpty)
            .Distinct()
            .Take(512)
            .ToArray();
        _rectangularOnly = rectangularOnly;
        _loupeTransform = new CompositeTransform();
        LoupeImage.RenderTransform = _loupeTransform;
        LoupeViewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 132, 156) };

        TableAction.IsEnabled = true;
        BarcodeAction.IsEnabled = true;
        ApplyActionLayout(actionLayout);
        ApplyOverlayModeVisuals();
        UpdateShapeModeUi();
        Closed += (_, _) => Complete(null);
    }

    private void ApplyActionLayout(IReadOnlyList<PersonalizationActionItem>? layout)
    {
        var map = new Dictionary<string, AppBarButton>(StringComparer.Ordinal)
        {
            ["copy"] = OverlayCopyAction,
            ["save"] = OverlaySaveAction,
            ["pin"] = OverlayPinAction,
            ["text"] = OverlayTextAction,
            ["table"] = TableAction,
            ["barcode"] = BarcodeAction,
            ["edit"] = OverlayEditAction,
            ["color"] = OverlayColorAction,
            ["magic"] = MagicAction
        };
        var effective = layout is { Count: > 0 } ? layout : AppSettingsRules.DefaultOverlayActions;
        SelectionActionCommandBar.PrimaryCommands.Clear();
        foreach (var item in effective)
            if (item.Visible && map.TryGetValue(item.Id, out var button)) SelectionActionCommandBar.PrimaryCommands.Add(button);
    }

    internal async Task<OverlaySelection?> SelectAsync()
    {
        WindowHelpers.MakeBorderlessTopmost(this);
        WindowHelpers.MoveAndResize(this, _monitor.Bounds.X, _monitor.Bounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height);
        Activate();
        await SetImageAsync(FrozenImage, _frozenPng);
        LoupeImage.Source = FrozenImage.Source;
        return await _completion.Task;
    }

    private PixelRect SourceBounds => new(0, 0, _monitor.Bounds.Width, _monitor.Bounds.Height);

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_resizing) return;
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (_selectionKind == CaptureSelectionKind.Polygon)
        {
            if (_pathFinalized) ResetSelectionContent();
            AddPathPoint(ToPhysical(point.Position));
            _pathHoverPoint = null;
            UpdateSelectionVisual();
            UpdateShapeStatus();
            e.Handled = true;
            return;
        }

        if (_selectionKind == CaptureSelectionKind.Rectangle && _snapEnabled && !_hoverSnapBounds.IsEmpty)
        {
            ResetSelectionContent();
            SetSelectionFromPhysical(_hoverSnapBounds);
            _hasSelection = true;
            HideSnapPreview();
            UpdateSelectionVisual();
            SetHandlesVisible(true);
            RefreshActionState();
            e.Handled = true;
            return;
        }

        if (_selectionKind != CaptureSelectionKind.MultiRegion)
            ResetSelectionContent(preservePointerState: true);

        _start = point.Position;
        _end = _start;
        _dragging = true;
        _resizing = false;
        _pathHoverPoint = null;
        HideSnapPreview();
        ActionBar.Visibility = Visibility.Collapsed;
        SetHandlesVisible(false);
        Root.CapturePointer(e.Pointer);

        if (_selectionKind == CaptureSelectionKind.Freehand)
        {
            _pathPoints.Clear();
            _pathFinalized = false;
            AddPathPoint(ToPhysical(point.Position));
        }
        else
        {
            SelectionRectangle.Visibility = Visibility.Visible;
        }
        SizeBadge.Visibility = Visibility.Visible;
        UpdateSelectionVisual();
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(Root).Position;
        UpdateLoupe(position);

        if (_selectionKind == CaptureSelectionKind.Polygon && !_pathFinalized)
        {
            _pathHoverPoint = ToPhysical(position);
            UpdatePathVisual();
            UpdateShapeStatus();
            return;
        }

        if (!_dragging)
        {
            UpdateWindowSnap(position);
            return;
        }

        HideSnapPreview();
        if (_selectionKind == CaptureSelectionKind.Freehand)
        {
            AddPathPoint(ToPhysical(position));
        }
        else if (_resizing)
        {
            ResizeSelectionTo(position);
        }
        else
        {
            _end = position;
        }
        UpdateSelectionVisual();
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var position = e.GetCurrentPoint(Root).Position;

        if (_selectionKind == CaptureSelectionKind.Freehand)
        {
            AddPathPoint(ToPhysical(position));
            _pathFinalized = CaptureSelectionGeometryRules.TryCreatePath(
                CaptureSelectionKind.Freehand, _pathPoints, SourceBounds, out _, out _);
        }
        else if (_resizing)
        {
            ResizeSelectionTo(position);
        }
        else
        {
            _end = position;
            if (_selectionKind == CaptureSelectionKind.Rectangle && _aspectRatio is null && _snapEnabled)
            {
                var snapped = CaptureSnapRules.SnapEdges(RawPhysicalBounds(), _edgeSnapBounds, SourceBounds);
                if (!snapped.IsEmpty) SetSelectionFromPhysical(snapped);
            }
        }

        _dragging = false;
        _resizing = false;
        Root.ReleasePointerCapture(e.Pointer);

        if (_selectionKind == CaptureSelectionKind.MultiRegion)
        {
            var region = RawPhysicalBounds();
            if (CaptureSelectionGeometryRules.TryCreateBox(
                    CaptureSelectionKind.Rectangle, region, SourceBounds, out var box, out _)
                && box is not null
                && _multiRegions.Count < CaptureSelectionGeometryRules.MaximumRegions
                && !_multiRegions.Contains(box.Bounds))
            {
                _multiRegions.Add(box.Bounds);
                RenderMultiRegionVisuals();
            }
            _start = default;
            _end = default;
            SelectionRectangle.Visibility = Visibility.Collapsed;
        }
        else if (_selectionKind is CaptureSelectionKind.Rectangle or CaptureSelectionKind.Ellipse)
        {
            _hasSelection = TryBuildGeometry(requireFinalized: true, out _, out _);
        }

        UpdateSelectionVisual();
        RefreshActionState();
    }

    private PixelPoint ToPhysical(Point position)
    {
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return new PixelPoint(0, 0);
        var x = (int)Math.Round(position.X * _monitor.Bounds.Width / Root.ActualWidth);
        var y = (int)Math.Round(position.Y * _monitor.Bounds.Height / Root.ActualHeight);
        return new PixelPoint(
            Math.Clamp(x, 0, Math.Max(0, _monitor.Bounds.Width - 1)),
            Math.Clamp(y, 0, Math.Max(0, _monitor.Bounds.Height - 1)));
    }

    private Point ToDip(PixelPoint point) => new(
        point.X * Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width),
        point.Y * Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height));

    private void AddPathPoint(PixelPoint point)
    {
        if (_pathPoints.Count > 0 && _pathPoints[^1] == point) return;
        if (_pathPoints.Count < CaptureSelectionGeometryRules.MaximumPathPoints)
        {
            _pathPoints.Add(point);
        }
        else
        {
            // Keep the path endpoint responsive without growing beyond the hard point budget.
            _pathPoints[^1] = point;
        }
    }

    private PixelRect RawPhysicalBounds()
    {
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return PixelRect.Empty;
        var scaleX = _monitor.Bounds.Width / Root.ActualWidth;
        var scaleY = _monitor.Bounds.Height / Root.ActualHeight;
        var sx = (int)Math.Round(_start.X * scaleX);
        var sy = (int)Math.Round(_start.Y * scaleY);
        var ex = (int)Math.Round(_end.X * scaleX);
        var ey = (int)Math.Round(_end.Y * scaleY);
        var left = Math.Min(sx, ex);
        var top = Math.Min(sy, ey);
        return new PixelRect(left, top, Math.Abs(ex - sx), Math.Abs(ey - sy)).Intersect(SourceBounds);
    }

    private PixelRect CurrentPhysicalBounds()
    {
        if (_selectionKind is CaptureSelectionKind.Polygon or CaptureSelectionKind.Freehand or CaptureSelectionKind.MultiRegion)
            return TryBuildGeometry(requireFinalized: false, out var geometry, out _) && geometry is not null
                ? geometry.Bounds
                : PixelRect.Empty;

        if (_aspectRatio is null || _selectionKind != CaptureSelectionKind.Rectangle)
        {
            var raw = RawPhysicalBounds();
            return _selectionKind == CaptureSelectionKind.Rectangle && _snapEnabled && _dragging && !_resizing
                ? CaptureSnapRules.SnapEdges(raw, _edgeSnapBounds, SourceBounds)
                : raw;
        }
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return PixelRect.Empty;
        var scaleX = _monitor.Bounds.Width / Root.ActualWidth;
        var scaleY = _monitor.Bounds.Height / Root.ActualHeight;
        var start = new PixelPoint((int)Math.Round(_start.X * scaleX), (int)Math.Round(_start.Y * scaleY));
        var end = new PixelPoint((int)Math.Round(_end.X * scaleX), (int)Math.Round(_end.Y * scaleY));
        return AspectLockedSelection.FromDrag(start, end, _aspectRatio.Value, SourceBounds);
    }

    private bool TryBuildGeometry(bool requireFinalized, out CaptureSelectionGeometry? geometry, out string? error)
    {
        geometry = null;
        error = null;
        switch (_selectionKind)
        {
            case CaptureSelectionKind.Rectangle:
            case CaptureSelectionKind.Ellipse:
                return CaptureSelectionGeometryRules.TryCreateBox(_selectionKind, CurrentBoxBounds(), SourceBounds, out geometry, out error);
            case CaptureSelectionKind.Polygon:
            case CaptureSelectionKind.Freehand:
                if (requireFinalized && !_pathFinalized)
                {
                    error = _selectionKind == CaptureSelectionKind.Polygon ? "Finish the polygon first." : "Draw a freehand region first.";
                    return false;
                }
                return CaptureSelectionGeometryRules.TryCreatePath(_selectionKind, _pathPoints, SourceBounds, out geometry, out error);
            case CaptureSelectionKind.MultiRegion:
                return CaptureSelectionGeometryRules.TryCreateMultiRegion(_multiRegions, SourceBounds, out geometry, out error);
            default:
                error = "Unsupported capture shape.";
                return false;
        }
    }

    private PixelRect CurrentBoxBounds()
    {
        if (_selectionKind == CaptureSelectionKind.Rectangle && _aspectRatio is { } ratio)
        {
            if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return PixelRect.Empty;
            var scaleX = _monitor.Bounds.Width / Root.ActualWidth;
            var scaleY = _monitor.Bounds.Height / Root.ActualHeight;
            var start = new PixelPoint((int)Math.Round(_start.X * scaleX), (int)Math.Round(_start.Y * scaleY));
            var end = new PixelPoint((int)Math.Round(_end.X * scaleX), (int)Math.Round(_end.Y * scaleY));
            return AspectLockedSelection.FromDrag(start, end, ratio, SourceBounds);
        }
        return RawPhysicalBounds();
    }

    private void UpdateSelectionVisual()
    {
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionEllipse.Visibility = Visibility.Collapsed;
        SelectionPath.Visibility = Visibility.Collapsed;
        SelectionPathPreview.Visibility = Visibility.Collapsed;

        switch (_selectionKind)
        {
            case CaptureSelectionKind.Rectangle:
                UpdateBoxVisual(SelectionRectangle, CurrentBoxBounds());
                break;
            case CaptureSelectionKind.Ellipse:
                UpdateBoxVisual(SelectionEllipse, CurrentBoxBounds());
                break;
            case CaptureSelectionKind.Polygon:
            case CaptureSelectionKind.Freehand:
                UpdatePathVisual();
                break;
            case CaptureSelectionKind.MultiRegion:
                if (_dragging) UpdateBoxVisual(SelectionRectangle, RawPhysicalBounds());
                break;
        }

        var physical = CurrentPhysicalBounds();
        if (physical.IsEmpty)
        {
            SizeBadge.Visibility = Visibility.Collapsed;
            SetHandlesVisible(false);
            UpdateShapeStatus();
            return;
        }

        var scaleX = Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width);
        var scaleY = Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height);
        var left = physical.X * scaleX;
        var top = physical.Y * scaleY;
        var desktopX = _monitor.Bounds.X + physical.X;
        var desktopY = _monitor.Bounds.Y + physical.Y;
        var detail = _selectionKind == CaptureSelectionKind.MultiRegion ? $" · {_multiRegions.Count} region(s)" : string.Empty;
        SizeText.Text = $"X {desktopX} · Y {desktopY} · W {physical.Width} · H {physical.Height} px · {_selectionKind}{detail}" +
                        (_selectionKind == CaptureSelectionKind.Rectangle && _aspectRatio is { } ratio ? $" · {FormatRatio(ratio)}" : string.Empty);
        Canvas.SetLeft(SizeBadge, Math.Max(8, left));
        Canvas.SetTop(SizeBadge, Math.Max(8, top - 34));
        SizeBadge.Visibility = Visibility.Visible;
        PositionResizeHandles(left, top, physical.Width * scaleX, physical.Height * scaleY);
        UpdateShapeStatus();
    }

    private void UpdateBoxVisual(FrameworkElement visual, PixelRect physical)
    {
        if (physical.IsEmpty) return;
        var scaleX = Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width);
        var scaleY = Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height);
        Canvas.SetLeft(visual, physical.X * scaleX);
        Canvas.SetTop(visual, physical.Y * scaleY);
        visual.Width = physical.Width * scaleX;
        visual.Height = physical.Height * scaleY;
        visual.Visibility = Visibility.Visible;
    }

    private void UpdatePathVisual()
    {
        var preview = !_pathFinalized;
        var targetPoints = preview ? SelectionPathPreview.Points : SelectionPath.Points;
        UIElement targetVisual = preview ? SelectionPathPreview : SelectionPath;
        targetPoints.Clear();
        foreach (var point in _pathPoints) targetPoints.Add(ToDip(point));
        if (preview && _selectionKind == CaptureSelectionKind.Polygon && _pathHoverPoint is { } hover && _pathPoints.Count > 0)
            targetPoints.Add(ToDip(hover));
        targetVisual.Visibility = targetPoints.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderMultiRegionVisuals()
    {
        foreach (var visual in _multiRegionVisuals) OverlayCanvas.Children.Remove(visual);
        _multiRegionVisuals.Clear();
        var stroke = SelectionStrokeBrush();
        var fill = SelectionFillBrush();
        foreach (var region in _multiRegions)
        {
            var visual = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Stroke = stroke,
                Fill = fill,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            _multiRegionVisuals.Add(visual);
            OverlayCanvas.Children.Add(visual);
            UpdateBoxVisual(visual, region);
        }
    }

    private void PositionActionBar()
    {
        var physical = CurrentPhysicalBounds();
        if (physical.IsEmpty) return;
        var scaleX = Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width);
        var scaleY = Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height);
        var left = physical.X * scaleX;
        var top = physical.Y * scaleY;
        var bottom = physical.Bottom * scaleY;
        var y = bottom + 8;
        if (y + 56 > Root.ActualHeight) y = Math.Max(8, top - 56);
        Canvas.SetLeft(ActionBar, Math.Clamp(left, 8, Math.Max(8, Root.ActualWidth - 720)));
        Canvas.SetTop(ActionBar, y);
    }

    private void RefreshActionState()
    {
        _hasSelection = TryBuildGeometry(requireFinalized: true, out _, out var error);
        SetHandlesVisible(_hasSelection);
        if (_hasSelection && !_dragging)
        {
            ActionBar.Visibility = Visibility.Visible;
            PositionActionBar();
        }
        else
        {
            ActionBar.Visibility = Visibility.Collapsed;
        }
        if (!_hasSelection && !string.IsNullOrWhiteSpace(error)) ShapeStatusText.Text = error;
        else UpdateShapeStatus();
    }

    private void SetAspect(double? ratio)
    {
        if (_selectionKind != CaptureSelectionKind.Rectangle) return;
        _aspectRatio = ratio;
        UpdateSelectionVisual();
        RefreshActionState();
    }

    private void FreeformRatio_Click(object sender, RoutedEventArgs e) => SetAspect(null);
    private void SquareRatio_Click(object sender, RoutedEventArgs e) => SetAspect(1.0);
    private void WideRatio_Click(object sender, RoutedEventArgs e) => SetAspect(16d / 9d);
    private void ClassicRatio_Click(object sender, RoutedEventArgs e) => SetAspect(4d / 3d);
    private static string FormatRatio(double ratio) => Math.Abs(ratio - 1) < 0.001 ? "1:1" : Math.Abs(ratio - 16d / 9d) < 0.001 ? "16:9" : "4:3";

    private void ResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_selectionKind is not CaptureSelectionKind.Rectangle and not CaptureSelectionKind.Ellipse) return;
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed || CurrentPhysicalBounds().IsEmpty) return;
        _resizeHandle = HandleFromElement(sender);
        if (_selectionKind == CaptureSelectionKind.Rectangle && _aspectRatio is not null && !SelectionHandleMath.IsCorner(_resizeHandle)) return;
        _resizeBasePhysical = CurrentPhysicalBounds();
        _resizing = true;
        _dragging = true;
        ActionBar.Visibility = Visibility.Collapsed;
        Root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeSelectionTo(Point position)
    {
        if (_resizeBasePhysical.IsEmpty || Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return;
        var cursor = ToPhysical(position);
        PixelRect resized;
        if (_selectionKind == CaptureSelectionKind.Rectangle && _aspectRatio is { } ratio && SelectionHandleMath.IsCorner(_resizeHandle))
        {
            var anchor = SelectionHandleMath.OppositeCorner(_resizeBasePhysical, _resizeHandle);
            resized = AspectLockedSelection.FromDrag(anchor, cursor, ratio, SourceBounds);
        }
        else
        {
            resized = SelectionHandleMath.Resize(_resizeBasePhysical, _resizeHandle, cursor, SourceBounds);
            if (_selectionKind == CaptureSelectionKind.Rectangle && _snapEnabled)
                resized = CaptureSnapRules.SnapEdges(resized, _edgeSnapBounds, SourceBounds);
        }
        SetSelectionFromPhysical(resized);
    }

    private void SetSelectionFromPhysical(PixelRect rect)
    {
        var scaleX = Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width);
        var scaleY = Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height);
        _start = new Point(rect.X * scaleX, rect.Y * scaleY);
        _end = new Point(rect.Right * scaleX, rect.Bottom * scaleY);
    }

    private SelectionResizeHandle HandleFromElement(object sender)
    {
        if (ReferenceEquals(sender, HandleNorthWest)) return SelectionResizeHandle.NorthWest;
        if (ReferenceEquals(sender, HandleNorth)) return SelectionResizeHandle.North;
        if (ReferenceEquals(sender, HandleNorthEast)) return SelectionResizeHandle.NorthEast;
        if (ReferenceEquals(sender, HandleEast)) return SelectionResizeHandle.East;
        if (ReferenceEquals(sender, HandleSouthEast)) return SelectionResizeHandle.SouthEast;
        if (ReferenceEquals(sender, HandleSouth)) return SelectionResizeHandle.South;
        if (ReferenceEquals(sender, HandleSouthWest)) return SelectionResizeHandle.SouthWest;
        if (ReferenceEquals(sender, HandleWest)) return SelectionResizeHandle.West;
        throw new InvalidOperationException("Unknown resize handle.");
    }

    private void SetHandlesVisible(bool visible)
    {
        visible &= _selectionKind is CaptureSelectionKind.Rectangle or CaptureSelectionKind.Ellipse;
        var cornerVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var edgeVisibility = visible && (_selectionKind == CaptureSelectionKind.Ellipse || _aspectRatio is null)
            ? Visibility.Visible
            : Visibility.Collapsed;
        HandleNorthWest.Visibility = cornerVisibility;
        HandleNorthEast.Visibility = cornerVisibility;
        HandleSouthEast.Visibility = cornerVisibility;
        HandleSouthWest.Visibility = cornerVisibility;
        HandleNorth.Visibility = edgeVisibility;
        HandleEast.Visibility = edgeVisibility;
        HandleSouth.Visibility = edgeVisibility;
        HandleWest.Visibility = edgeVisibility;
    }

    private void PositionResizeHandles(double left, double top, double width, double height)
    {
        const double half = 5;
        var right = left + width;
        var bottom = top + height;
        var centerX = left + width / 2;
        var centerY = top + height / 2;
        PositionHandle(HandleNorthWest, left - half, top - half);
        PositionHandle(HandleNorth, centerX - half, top - half);
        PositionHandle(HandleNorthEast, right - half, top - half);
        PositionHandle(HandleEast, right - half, centerY - half);
        PositionHandle(HandleSouthEast, right - half, bottom - half);
        PositionHandle(HandleSouth, centerX - half, bottom - half);
        PositionHandle(HandleSouthWest, left - half, bottom - half);
        PositionHandle(HandleWest, left - half, centerY - half);
    }

    private static void PositionHandle(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private void RectangleShape_Click(object sender, RoutedEventArgs e) => SetSelectionKind(CaptureSelectionKind.Rectangle);
    private void EllipseShape_Click(object sender, RoutedEventArgs e) => SetSelectionKind(CaptureSelectionKind.Ellipse);
    private void PolygonShape_Click(object sender, RoutedEventArgs e) => SetSelectionKind(CaptureSelectionKind.Polygon);
    private void FreehandShape_Click(object sender, RoutedEventArgs e) => SetSelectionKind(CaptureSelectionKind.Freehand);
    private void MultiRegionShape_Click(object sender, RoutedEventArgs e) => SetSelectionKind(CaptureSelectionKind.MultiRegion);

    private void SetSelectionKind(CaptureSelectionKind kind)
    {
        if (_dragging || (_rectangularOnly && kind != CaptureSelectionKind.Rectangle)) return;
        _selectionKind = kind;
        _aspectRatio = null;
        if (kind != CaptureSelectionKind.MultiRegion) _multiRegionOutput = MultiRegionOutputMode.Canvas;
        ResetSelectionContent();
        UpdateShapeModeUi();
        ApplyOverlayModeVisuals();
    }

    private void FinishShape_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionKind != CaptureSelectionKind.Polygon) return;
        _pathHoverPoint = null;
        _pathFinalized = CaptureSelectionGeometryRules.TryCreatePath(
            CaptureSelectionKind.Polygon, _pathPoints, SourceBounds, out _, out var error);
        UpdateSelectionVisual();
        RefreshActionState();
        if (!_pathFinalized && !string.IsNullOrWhiteSpace(error)) ShapeStatusText.Text = error;
    }

    private void UndoShape_Click(object sender, RoutedEventArgs e)
    {
        switch (_selectionKind)
        {
            case CaptureSelectionKind.Polygon:
                if (_pathPoints.Count > 0) _pathPoints.RemoveAt(_pathPoints.Count - 1);
                _pathFinalized = false;
                _pathHoverPoint = null;
                break;
            case CaptureSelectionKind.Freehand:
                _pathPoints.Clear();
                _pathFinalized = false;
                break;
            case CaptureSelectionKind.MultiRegion:
                if (_multiRegions.Count > 0) _multiRegions.RemoveAt(_multiRegions.Count - 1);
                RenderMultiRegionVisuals();
                break;
        }
        UpdateSelectionVisual();
        RefreshActionState();
    }

    private void MultiRegionOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionKind != CaptureSelectionKind.MultiRegion) return;
        _multiRegionOutput = _multiRegionOutput == MultiRegionOutputMode.Canvas
            ? MultiRegionOutputMode.SeparateImages
            : MultiRegionOutputMode.Canvas;
        UpdateShapeModeUi();
    }

    private void Reselect_Click(object sender, RoutedEventArgs e) => ResetSelectionContent();

    private void ResetSelectionContent(bool preservePointerState = false)
    {
        if (!preservePointerState)
        {
            _dragging = false;
            _resizing = false;
        }
        _hasSelection = false;
        _start = default;
        _end = default;
        _pathPoints.Clear();
        _multiRegions.Clear();
        _pathHoverPoint = null;
        _pathFinalized = false;
        _resizeBasePhysical = PixelRect.Empty;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionEllipse.Visibility = Visibility.Collapsed;
        SelectionPath.Visibility = Visibility.Collapsed;
        SelectionPathPreview.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        SetHandlesVisible(false);
        _hoverSnapBounds = PixelRect.Empty;
        HideSnapPreview();
        RenderMultiRegionVisuals();
        UpdateShapeStatus();
    }

    private void UpdateShapeModeUi()
    {
        RectangleShapeButton.Content = _selectionKind == CaptureSelectionKind.Rectangle ? "● Rectangle" : "Rectangle";
        EllipseShapeButton.Visibility = _rectangularOnly ? Visibility.Collapsed : Visibility.Visible;
        PolygonShapeButton.Visibility = _rectangularOnly ? Visibility.Collapsed : Visibility.Visible;
        FreehandShapeButton.Visibility = _rectangularOnly ? Visibility.Collapsed : Visibility.Visible;
        MultiRegionShapeButton.Visibility = _rectangularOnly ? Visibility.Collapsed : Visibility.Visible;
        EllipseShapeButton.Content = _selectionKind == CaptureSelectionKind.Ellipse ? "● Ellipse" : "Ellipse";
        PolygonShapeButton.Content = _selectionKind == CaptureSelectionKind.Polygon ? "● Polygon" : "Polygon";
        FreehandShapeButton.Content = _selectionKind == CaptureSelectionKind.Freehand ? "● Freehand" : "Freehand";
        MultiRegionShapeButton.Content = _selectionKind == CaptureSelectionKind.MultiRegion ? "● Multi-region" : "Multi-region";

        var rectangle = _selectionKind == CaptureSelectionKind.Rectangle;
        FreeformRatioButton.IsEnabled = rectangle;
        var pro = _tier == ProductTier.ProLifetime;
        SquareRatioButton.IsEnabled = rectangle && pro;
        WideRatioButton.IsEnabled = rectangle && pro;
        ClassicRatioButton.IsEnabled = rectangle && pro;
        var snapAvailable = _controlSnapTargets.Count > 0 || _windowSnapBounds.Count > 0;
        SnapModeButton.IsEnabled = rectangle && snapAvailable;
        _snapEnabled = rectangle && snapAvailable && _snapUserPreference;
        SnapModeButton.Content = !snapAvailable ? "Smart snap unavailable" : _snapEnabled ? "Smart snap on" : "Smart snap off";
        FinishShapeButton.Visibility = _selectionKind == CaptureSelectionKind.Polygon ? Visibility.Visible : Visibility.Collapsed;
        UndoShapeButton.Visibility = _selectionKind is CaptureSelectionKind.Polygon or CaptureSelectionKind.Freehand or CaptureSelectionKind.MultiRegion
            ? Visibility.Visible
            : Visibility.Collapsed;

        MultiRegionOutputButton.Visibility = _selectionKind == CaptureSelectionKind.MultiRegion ? Visibility.Visible : Visibility.Collapsed;
        MultiRegionOutputButton.Content = _multiRegionOutput == MultiRegionOutputMode.Canvas ? "Output: Canvas" : "Output: Separate images";

        OverlayHelpText.Text = _selectionKind switch
        {
            CaptureSelectionKind.Rectangle when _rectangularOnly => "Drag the scrolling rectangle · resize handles · Enter confirms · Esc cancels",
            CaptureSelectionKind.Rectangle => "Drag rectangle · resize handles · arrows nudge · Shift+arrows 10 px · C copy · S save · Esc cancel",
            CaptureSelectionKind.Ellipse => "Drag ellipse · resize its bounding box · C copy · S save · Esc cancel",
            CaptureSelectionKind.Polygon => "Click vertices · Finish shape (or Enter) · Undo removes last vertex · Esc cancel",
            CaptureSelectionKind.Freehand => "Press and draw a closed freehand region · release to finish · Esc cancel",
            CaptureSelectionKind.MultiRegion => _multiRegionOutput == MultiRegionOutputMode.Canvas
                ? $"Drag up to {CaptureSelectionGeometryRules.MaximumRegions} rectangles · Undo removes last region · output is one transparent canvas"
                : $"Drag up to {CaptureSelectionGeometryRules.MaximumRegions} rectangles · Separate images: Open sends all to History · Save exports one folder · Workflow runs per image",
            _ => string.Empty
        };
        UpdateShapeStatus();
    }

    private void UpdateShapeStatus()
    {
        ShapeStatusText.Text = _selectionKind switch
        {
            CaptureSelectionKind.Polygon => _pathFinalized ? $"Polygon · {_pathPoints.Count} vertices" : $"Polygon · {_pathPoints.Count} vertices",
            CaptureSelectionKind.Freehand => $"Freehand · {_pathPoints.Count} samples",
            CaptureSelectionKind.MultiRegion => $"{_multiRegions.Count}/{CaptureSelectionGeometryRules.MaximumRegions} regions · {(_multiRegionOutput == MultiRegionOutputMode.Canvas ? "canvas" : "separate")}",
            CaptureSelectionKind.Ellipse => "Ellipse",
            _ => "Rectangle"
        };
    }

    private void SnapMode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionKind != CaptureSelectionKind.Rectangle || (_controlSnapTargets.Count == 0 && _windowSnapBounds.Count == 0)) return;
        _snapUserPreference = !_snapUserPreference;
        _snapEnabled = _snapUserPreference;
        SnapModeButton.Content = _snapEnabled ? "Smart snap on" : "Smart snap off";
        if (!_snapEnabled)
        {
            _hoverSnapBounds = PixelRect.Empty;
            HideSnapPreview();
        }
    }

    private void UpdateWindowSnap(Point position)
    {
        if (_selectionKind != CaptureSelectionKind.Rectangle || !_snapEnabled || _hasSelection || Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
        {
            HideSnapPreview();
            return;
        }

        var physical = ToPhysical(position);
        var controlTarget = UiAutomationSnapshotRules.FindSnapTarget(_controlSnapTargets, physical);
        if (controlTarget is not null)
        {
            _hoverSnapBounds = controlTarget.Bounds;
            ShowSnapPreview(_hoverSnapBounds, controlTarget.Label);
            return;
        }

        _hoverSnapBounds = CaptureSnapRules.SelectSmallestContaining(_windowSnapBounds, physical);
        if (_hoverSnapBounds.IsEmpty)
        {
            HideSnapPreview();
            return;
        }
        ShowSnapPreview(_hoverSnapBounds, "Window");
    }

    private void ShowSnapPreview(PixelRect bounds, string label)
    {
        if (bounds.IsEmpty)
        {
            HideSnapPreview();
            return;
        }
        var scaleX = Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width);
        var scaleY = Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height);
        var left = bounds.X * scaleX;
        var top = bounds.Y * scaleY;
        Canvas.SetLeft(SnapRectangle, left);
        Canvas.SetTop(SnapRectangle, top);
        SnapRectangle.Width = bounds.Width * scaleX;
        SnapRectangle.Height = bounds.Height * scaleY;
        SnapRectangle.Visibility = Visibility.Visible;

        SnapLabelText.Text = label;
        var labelLeft = Math.Clamp(left, 8, Math.Max(8, Root.ActualWidth - 320));
        var labelTop = top >= 34 ? top - 30 : Math.Min(Root.ActualHeight - 32, top + bounds.Height * scaleY + 6);
        Canvas.SetLeft(SnapLabelBadge, labelLeft);
        Canvas.SetTop(SnapLabelBadge, Math.Max(8, labelTop));
        SnapLabelBadge.Visibility = Visibility.Visible;
    }

    private void HideSnapPreview()
    {
        SnapRectangle.Visibility = Visibility.Collapsed;
        SnapLabelBadge.Visibility = Visibility.Collapsed;
    }

    private void UpdateLoupe(Point position)
    {
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0 || FrozenImage.Source is null) return;
        const double zoom = 6.0;
        const double viewport = 132.0;
        LoupeImage.Width = Root.ActualWidth;
        LoupeImage.Height = Root.ActualHeight;
        _loupeTransform.ScaleX = zoom;
        _loupeTransform.ScaleY = zoom;
        _loupeTransform.TranslateX = viewport / 2 - position.X * zoom;
        _loupeTransform.TranslateY = viewport / 2 - position.Y * zoom;
        var left = position.X + 24;
        var top = position.Y + 24;
        if (left + 140 > Root.ActualWidth) left = Math.Max(8, position.X - 156);
        if (top + 164 > Root.ActualHeight) top = Math.Max(8, position.Y - 180);
        Canvas.SetLeft(LoupeViewport, left);
        Canvas.SetTop(LoupeViewport, top);
        var px = ToPhysical(position);
        LoupeCoordinateText.Text = $"{_monitor.Bounds.X + px.X}, {_monitor.Bounds.Y + px.Y} · 6×";
        LoupeViewport.Visibility = Visibility.Visible;
    }

    private void OverlayMode_Click(object sender, RoutedEventArgs e)
    {
        _lightOverlay = !_lightOverlay;
        ApplyOverlayModeVisuals();
    }

    private SolidColorBrush SelectionStrokeBrush() => new(_lightOverlay
        ? Windows.UI.Color.FromArgb(255, 0, 0, 0)
        : Windows.UI.Color.FromArgb(255, 255, 255, 255));

    private SolidColorBrush SelectionFillBrush() => new(_lightOverlay
        ? Windows.UI.Color.FromArgb(24, 255, 255, 255)
        : Windows.UI.Color.FromArgb(18, 0, 0, 0));

    private void ApplyOverlayModeVisuals()
    {
        FrozenImage.Opacity = _lightOverlay ? 0.9 : 0.72;
        Root.Background = new SolidColorBrush(_lightOverlay
            ? Windows.UI.Color.FromArgb(255, 245, 245, 245)
            : Windows.UI.Color.FromArgb(255, 0, 0, 0));
        var stroke = SelectionStrokeBrush();
        var fill = SelectionFillBrush();
        SelectionRectangle.Stroke = stroke;
        SelectionRectangle.Fill = fill;
        SelectionEllipse.Stroke = stroke;
        SelectionEllipse.Fill = fill;
        SelectionPath.Stroke = stroke;
        SelectionPath.Fill = fill;
        SelectionPathPreview.Stroke = stroke;
        OverlayModeButton.Content = _lightOverlay ? "Dark overlay" : "Light overlay";
        RenderMultiRegionVisuals();
    }

    private void ActionBar_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;

    private void NudgeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_selectionKind is not CaptureSelectionKind.Rectangle and not CaptureSelectionKind.Ellipse) return;
        if (_dragging || CurrentPhysicalBounds().IsEmpty) return;
        var physicalStep = sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift) ? 10 : 1;
        var dxPhysical = sender.Key == VirtualKey.Left ? -physicalStep : sender.Key == VirtualKey.Right ? physicalStep : 0;
        var dyPhysical = sender.Key == VirtualKey.Up ? -physicalStep : sender.Key == VirtualKey.Down ? physicalStep : 0;
        var dxDip = dxPhysical * Root.ActualWidth / Math.Max(1, _monitor.Bounds.Width);
        var dyDip = dyPhysical * Root.ActualHeight / Math.Max(1, _monitor.Bounds.Height);
        var current = CurrentPhysicalBounds();
        if (current.X + dxPhysical < 0 || current.Right + dxPhysical > _monitor.Bounds.Width) dxDip = 0;
        if (current.Y + dyPhysical < 0 || current.Bottom + dyPhysical > _monitor.Bounds.Height) dyDip = 0;
        _start = new Point(_start.X + dxDip, _start.Y + dyDip);
        _end = new Point(_end.X + dxDip, _end.Y + dyDip);
        UpdateSelectionVisual();
        PositionActionBar();
        args.Handled = true;
    }

    private void CompleteAction(OverlayCaptureAction action, string? workflowId = null)
    {
        if (!TryBuildGeometry(requireFinalized: true, out var geometry, out var error) || geometry is null)
        {
            ShapeStatusText.Text = error ?? "Complete a valid capture selection first.";
            return;
        }
        if (geometry.Kind == CaptureSelectionKind.MultiRegion && _multiRegionOutput == MultiRegionOutputMode.SeparateImages &&
            action is not OverlayCaptureAction.Result and not OverlayCaptureAction.Save and not OverlayCaptureAction.Workflow)
        {
            ShapeStatusText.Text = "Separate-image mode supports Open, Save, or Workflow. Use Canvas output for one-image actions such as Copy, Pin, Edit, Text, Color, or Magic.";
            return;
        }
        if (action == OverlayCaptureAction.Color && geometry.Kind is CaptureSelectionKind.Polygon or CaptureSelectionKind.Freehand or CaptureSelectionKind.MultiRegion)
        {
            ShapeStatusText.Text = "Color samples one center pixel. Use Rectangle or Ellipse around the pixel you want to sample.";
            return;
        }
        Complete(new OverlaySelection(geometry, action, workflowId, _multiRegionOutput));
    }

    private void Complete(OverlaySelection? selection)
    {
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(selection);
        Close();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Result);
    private void CopyButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Copy);
    private void PinButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Pin);
    private void SaveButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Save);
    private void TextButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Text);
    private void TableButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Table);
    private void BarcodeButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Barcode);
    private void EditButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Edit);
    private void ColorButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Color);
    private void MagicButton_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Magic);
    private void QuickCopyWorkflow_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Workflow, "quick-copy");
    private void OcrWorkflow_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Workflow, "ocr-copy");
    private void DocumentationWorkflow_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Workflow, "documentation");
    private void DataWorkflow_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Workflow, "data-capture");
    private void BugReportWorkflow_Click(object sender, RoutedEventArgs e) => CompleteAction(OverlayCaptureAction.Workflow, "bug-report");
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Complete(null);

    private void CancelAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Complete(null);
    }

    private void ConfirmAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_selectionKind == CaptureSelectionKind.Polygon && !_pathFinalized)
        {
            FinishShape_Click(this, new RoutedEventArgs());
            return;
        }
        CompleteAction(_defaultAction, _defaultAction == OverlayCaptureAction.Workflow ? _defaultWorkflowId : null);
    }

    private void UndoShapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_selectionKind is not CaptureSelectionKind.Polygon and not CaptureSelectionKind.Freehand and not CaptureSelectionKind.MultiRegion) return;
        args.Handled = true;
        UndoShape_Click(this, new RoutedEventArgs());
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; CompleteAction(OverlayCaptureAction.Copy); }
    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; CompleteAction(OverlayCaptureAction.Save); }
    private void PinAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; CompleteAction(OverlayCaptureAction.Pin); }
    private void TextAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; CompleteAction(OverlayCaptureAction.Text); }
    private void EditAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; CompleteAction(OverlayCaptureAction.Edit); }
    private void MagicAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; CompleteAction(OverlayCaptureAction.Magic); }

    internal static async Task SetImageAsync(Image target, byte[] pngBytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        target.Source = image;
    }
}
