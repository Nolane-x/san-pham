using Magic.Capture.App.Capture;
using Magic.Capture.App.Platform;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace Magic.Capture.App.Views;

internal enum MeasurementOverlayMode { Ruler, Focus, Whiteboard }

public sealed partial class MeasurementOverlayWindow : Window
{
    private readonly ApplicationServices _services;
    private readonly MeasurementOverlayMode _mode;
    private readonly double _dpi;
    private PixelRect _desktopBounds;
    private Point _start;
    private bool _dragging;
    private Polyline? _activeStroke;

    internal MeasurementOverlayWindow(ApplicationServices services, MeasurementOverlayMode mode, double dpi = 96)
    {
        InitializeComponent();
        _services = services;
        _mode = mode;
        _dpi = double.IsFinite(dpi) ? Math.Clamp(dpi, 10, 2_000) : 96;
        ModeText.Text = mode switch { MeasurementOverlayMode.Focus => "Screen Focus", MeasurementOverlayMode.Whiteboard => "Whiteboard", _ => $"Ruler · {_dpi:F1} DPI" };
        Activated += OnActivated;
        Root.SizeChanged += Root_SizeChanged;
        Closed += OnClosed;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated || DesktopImage.Source is not null) return;
        try
        {
            _desktopBounds = _services.Monitors.GetVirtualScreenBounds();
            var asset = _services.ScreenCapture.Capture(_desktopBounds, CaptureSourceKind.VirtualDesktop, "Measurement overlay");
            WindowHelpers.MakeBorderlessTopmost(this);
            WindowHelpers.MoveAndResize(this, _desktopBounds.X, _desktopBounds.Y, _desktopBounds.Width, _desktopBounds.Height);
            await CaptureOverlayWindow.SetImageAsync(DesktopImage, asset.PngBytes);
            UpdateCanvasSize();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { MeasurementText.Text = ex.Message; }
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCanvasSize();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Root.SizeChanged -= Root_SizeChanged;
        Closed -= OnClosed;
        _activeStroke = null;
    }

    private void UpdateCanvasSize()
    {
        OverlayCanvas.Width = Math.Max(1, Root.ActualWidth);
        OverlayCanvas.Height = Math.Max(1, Root.ActualHeight);
        CrosshairH.X1 = 0; CrosshairH.X2 = OverlayCanvas.Width;
        CrosshairV.Y1 = 0; CrosshairV.Y2 = OverlayCanvas.Height;
    }

    private PixelPoint ToPhysical(Point point)
    {
        var sx = _desktopBounds.Width / Math.Max(1d, Root.ActualWidth);
        var sy = _desktopBounds.Height / Math.Max(1d, Root.ActualHeight);
        return new PixelPoint((int)Math.Round(point.X * sx), (int)Math.Round(point.Y * sy));
    }

    private void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var current = e.GetCurrentPoint(OverlayCanvas);
        if (!current.Properties.IsLeftButtonPressed) return;
        _start = current.Position;
        _dragging = true;
        OverlayCanvas.CapturePointer(e.Pointer);
        if (_mode == MeasurementOverlayMode.Whiteboard)
        {
            _activeStroke = new Polyline { Stroke = new SolidColorBrush(Microsoft.UI.Colors.Red), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
            _activeStroke.Points.Add(_start);
            OverlayCanvas.Children.Add(_activeStroke);
        }
        else UpdateDrag(_start);
        e.Handled = true;
    }

    private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(OverlayCanvas).Position;
        CrosshairH.Y1 = CrosshairH.Y2 = point.Y;
        CrosshairV.X1 = CrosshairV.X2 = point.X;
        var physical = ToPhysical(point);
        MeasurementText.Text = $"X {_desktopBounds.X + physical.X} · Y {_desktopBounds.Y + physical.Y}";
        if (!_dragging) return;
        if (_mode == MeasurementOverlayMode.Whiteboard)
        {
            if (_activeStroke is { } stroke && stroke.Points.Count < 8_192) stroke.Points.Add(point);
        }
        else UpdateDrag(point);
    }

    private void Overlay_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        OverlayCanvas.ReleasePointerCapture(e.Pointer);
        if (_mode != MeasurementOverlayMode.Whiteboard) UpdateDrag(e.GetCurrentPoint(OverlayCanvas).Position);
        _activeStroke = null;
    }

    private void UpdateDrag(Point end)
    {
        if (_mode == MeasurementOverlayMode.Ruler)
        {
            MeasureLine.Visibility = HorizontalLine.Visibility = VerticalLine.Visibility = Visibility.Visible;
            MeasureLine.X1 = _start.X; MeasureLine.Y1 = _start.Y; MeasureLine.X2 = end.X; MeasureLine.Y2 = end.Y;
            HorizontalLine.X1 = _start.X; HorizontalLine.Y1 = _start.Y; HorizontalLine.X2 = end.X; HorizontalLine.Y2 = _start.Y;
            VerticalLine.X1 = end.X; VerticalLine.Y1 = _start.Y; VerticalLine.X2 = end.X; VerticalLine.Y2 = end.Y;
            var a = ToPhysical(_start); var b = ToPhysical(end);
            var m = ScreenMeasurement.Measure(a, b, _dpi);
            MeasurementText.Text = $"ΔX {m.DeltaX:+#;-#;0}px · ΔY {m.DeltaY:+#;-#;0}px · {m.DistancePixels:F1}px · {m.Inches:F3}in · {m.Centimeters:F2}cm · {m.AngleDegrees:F1}°";
        }
        else if (_mode == MeasurementOverlayMode.Focus)
        {
            var left = Math.Min(_start.X, end.X); var top = Math.Min(_start.Y, end.Y);
            var right = Math.Max(_start.X, end.X); var bottom = Math.Max(_start.Y, end.Y);
            SelectionBox.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBox, left); Canvas.SetTop(SelectionBox, top); SelectionBox.Width = Math.Max(1, right - left); SelectionBox.Height = Math.Max(1, bottom - top);
            ApplyFocusMasks(left, top, right, bottom);
            var a = ToPhysical(_start); var b = ToPhysical(end);
            MeasurementText.Text = $"Focus {Math.Abs(b.X-a.X)}×{Math.Abs(b.Y-a.Y)} px";
        }
    }

    private void ApplyFocusMasks(double left, double top, double right, double bottom)
    {
        foreach (var rect in new[] { FocusTop, FocusLeft, FocusRight, FocusBottom }) rect.Visibility = Visibility.Visible;
        SetRect(FocusTop, 0, 0, OverlayCanvas.Width, Math.Max(0, top));
        SetRect(FocusBottom, 0, bottom, OverlayCanvas.Width, Math.Max(0, OverlayCanvas.Height - bottom));
        SetRect(FocusLeft, 0, top, Math.Max(0, left), Math.Max(0, bottom - top));
        SetRect(FocusRight, right, top, Math.Max(0, OverlayCanvas.Width - right), Math.Max(0, bottom - top));
    }

    private static void SetRect(Rectangle rect, double x, double y, double width, double height)
    {
        Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); rect.Width = width; rect.Height = height;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var child in OverlayCanvas.Children.OfType<Polyline>().ToArray()) OverlayCanvas.Children.Remove(child);
        MeasureLine.Visibility = HorizontalLine.Visibility = VerticalLine.Visibility = SelectionBox.Visibility = Visibility.Collapsed;
        foreach (var rect in new[] { FocusTop, FocusLeft, FocusRight, FocusBottom }) rect.Visibility = Visibility.Collapsed;
        MeasurementText.Text = string.Empty;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Close(); }
}
