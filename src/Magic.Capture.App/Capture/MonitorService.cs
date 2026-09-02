using System.Runtime.InteropServices;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Capture;

namespace Magic.Capture.App.Capture;

internal sealed class MonitorService
{
    public MonitorInfo GetActiveMonitor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            throw new InvalidOperationException("Unable to read the current pointer position.");
        var handle = NativeMethods.MonitorFromPoint(point, NativeConstants.MonitorDefaultToNearest);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Unable to resolve the active monitor.");

        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };
        if (!NativeMethods.GetMonitorInfo(handle, ref info))
            throw new InvalidOperationException("Unable to query active monitor bounds.");

        var dpi = ReadEffectiveDpi(handle);
        return new MonitorInfo(
            handle,
            FromNative(info.Monitor),
            FromNative(info.WorkArea),
            (info.Flags & NativeConstants.MonitorInfoPrimary) != 0,
            info.DeviceName ?? string.Empty,
            dpi.X,
            dpi.Y);
    }


    public IReadOnlyList<MonitorInfo> ListMonitors()
    {
        var monitors = new List<MonitorInfo>();
        _ = NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
            if (!NativeMethods.GetMonitorInfo(handle, ref info)) return true;
            var dpi = ReadEffectiveDpi(handle);
            monitors.Add(new MonitorInfo(handle, FromNative(info.Monitor), FromNative(info.WorkArea), (info.Flags & NativeConstants.MonitorInfoPrimary) != 0, info.DeviceName ?? string.Empty, dpi.X, dpi.Y));
            return true;
        }, IntPtr.Zero);
        return monitors.OrderByDescending(monitor => monitor.IsPrimary).ThenBy(monitor => monitor.Bounds.X).ThenBy(monitor => monitor.Bounds.Y).ToArray();
    }


    public MonitorInfo? FindContainingMonitor(PixelRect bounds) =>
        ListMonitors().FirstOrDefault(monitor =>
            !bounds.IsEmpty && bounds.X >= monitor.Bounds.X && bounds.Y >= monitor.Bounds.Y &&
            bounds.Right <= monitor.Bounds.Right && bounds.Bottom <= monitor.Bounds.Bottom);

    public MonitorInfo? FindMonitorByBounds(PixelRect bounds) =>
        ListMonitors().FirstOrDefault(monitor => monitor.Bounds == bounds);

    public PixelRect GetVirtualScreenBounds() => new(
        NativeMethods.GetSystemMetrics(NativeConstants.SmXVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeConstants.SmYVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeConstants.SmCxVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeConstants.SmCyVirtualScreen));

    public DesktopPixelTopology GetDesktopPixelTopology()
    {
        var monitors = ListMonitors();
        return new DesktopPixelTopology(
            GetVirtualScreenBounds(),
            monitors.Select(monitor => new DesktopPixelMonitor(
                string.IsNullOrWhiteSpace(monitor.DeviceName) ? $"monitor-{monitor.Handle}" : monitor.DeviceName,
                monitor.Bounds,
                monitor.DpiX,
                monitor.DpiY,
                monitor.IsPrimary)).ToArray());
    }

    public PixelRect ToDesktopBounds(MonitorInfo monitor, PixelRect localBounds)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var model = new DesktopPixelMonitor(
            string.IsNullOrWhiteSpace(monitor.DeviceName) ? $"monitor-{monitor.Handle}" : monitor.DeviceName,
            monitor.Bounds, monitor.DpiX, monitor.DpiY, monitor.IsPrimary);
        var topology = new DesktopPixelTopology(GetVirtualScreenBounds(), [model]);
        return topology.ToDesktopBounds(model, localBounds);
    }

    private static (double X, double Y) ReadEffectiveDpi(IntPtr handle)
    {
        try
        {
            var hr = NativeMethods.GetDpiForMonitor(handle, NativeConstants.MdtEffectiveDpi, out var dpiX, out var dpiY);
            if (hr == 0 && dpiX > 0 && dpiY > 0) return (dpiX, dpiY);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return (96, 96);
    }

    private static PixelRect FromNative(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
