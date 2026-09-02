using System.Text;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal sealed class RecordingFrameProvider
{
    private readonly ScreenCaptureService _screen;
    private readonly MonitorService _monitors;

    public RecordingFrameProvider(ScreenCaptureService screen, MonitorService monitors)
    {
        _screen = screen;
        _monitors = monitors;
    }

    public CaptureAsset Capture(RecordingTarget target, bool includeCursor)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Kind switch
        {
            RecordingTargetKind.Region => CaptureRegion(target, includeCursor),
            RecordingTargetKind.Window => CaptureWindow(target, includeCursor),
            RecordingTargetKind.Monitor => CaptureMonitor(target, includeCursor),
            RecordingTargetKind.VirtualDesktop => CaptureVirtualDesktop(target, includeCursor),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }

    private CaptureAsset CaptureRegion(RecordingTarget target, bool includeCursor)
    {
        var virtualBounds = _monitors.GetVirtualScreenBounds();
        var bounds = target.Bounds.Intersect(virtualBounds);
        if (bounds != target.Bounds || bounds.IsEmpty)
            throw new InvalidOperationException("The recording region is no longer fully visible on the current desktop layout.");
        var monitor = _monitors.FindContainingMonitor(bounds);
        return _screen.Capture(
            bounds,
            CaptureSourceKind.Region,
            target.DisplayName,
            includeCursor,
            monitorName: monitor?.DeviceName,
            monitorHandle: monitor?.Handle ?? IntPtr.Zero,
            sourceBounds: monitor?.Bounds,
            targetKind: monitor is null ? CaptureTargetKind.RegionCrossMonitor : CaptureTargetKind.RegionSingleMonitor);
    }

    private CaptureAsset CaptureWindow(RecordingTarget target, bool includeCursor)
    {
        if (!NativeMethods.GetWindowRect(target.WindowHandle, out var native))
            throw new InvalidOperationException("The recorded window is no longer available.");
        var requested = new PixelRect(native.Left, native.Top, Math.Max(0, native.Right - native.Left), Math.Max(0, native.Bottom - native.Top));
        var visible = requested.Intersect(_monitors.GetVirtualScreenBounds());
        if (visible.IsEmpty) throw new InvalidOperationException("The recorded window is outside the visible desktop.");
        if (visible.Width != target.Bounds.Width || visible.Height != target.Bounds.Height)
            throw new InvalidOperationException("The recorded window changed size. Stop and start a new recording for the new dimensions.");

        var titleLength = Math.Clamp(NativeMethods.GetWindowTextLength(target.WindowHandle) + 1, 2, 2048);
        var titleBuilder = new StringBuilder(titleLength);
        _ = NativeMethods.GetWindowText(target.WindowHandle, titleBuilder, titleBuilder.Capacity);
        var title = string.IsNullOrWhiteSpace(titleBuilder.ToString()) ? target.DisplayName : titleBuilder.ToString().Trim();
        return _screen.Capture(
            visible,
            CaptureSourceKind.Window,
            title,
            includeCursor,
            windowTitle: title,
            windowHandle: target.WindowHandle,
            sourceBounds: visible,
            targetKind: CaptureTargetKind.Window);
    }

    private CaptureAsset CaptureMonitor(RecordingTarget target, bool includeCursor)
    {
        var monitor = _monitors.ListMonitors().FirstOrDefault(item => item.Handle == target.MonitorHandle)
            ?? throw new InvalidOperationException("The recorded monitor is no longer available.");
        if (monitor.Bounds.Width != target.Bounds.Width || monitor.Bounds.Height != target.Bounds.Height)
            throw new InvalidOperationException("The recorded monitor changed resolution. Stop and start a new recording.");
        return _screen.Capture(
            monitor.Bounds,
            CaptureSourceKind.Monitor,
            monitor.DeviceName,
            includeCursor,
            monitorName: monitor.DeviceName,
            monitorHandle: monitor.Handle,
            sourceBounds: monitor.Bounds,
            targetKind: CaptureTargetKind.Monitor);
    }

    private CaptureAsset CaptureVirtualDesktop(RecordingTarget target, bool includeCursor)
    {
        var current = _monitors.GetVirtualScreenBounds();
        if (current != target.Bounds)
            throw new InvalidOperationException("The virtual desktop layout changed during recording. Stop and start a new recording.");
        return _screen.Capture(current, CaptureSourceKind.VirtualDesktop, "Virtual Desktop", includeCursor,
            targetKind: CaptureTargetKind.VirtualDesktop);
    }
}
