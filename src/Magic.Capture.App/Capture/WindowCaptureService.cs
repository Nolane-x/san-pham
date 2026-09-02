using System.Diagnostics;
using System.Text;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Capture;

internal sealed class WindowCaptureService
{
    private const int MaximumCatalogWindows = 256;
    private readonly ScreenCaptureService _screenCapture;
    private readonly MonitorService _monitors;

    public WindowCaptureService(ScreenCaptureService screenCapture, MonitorService monitors)
    {
        _screenCapture = screenCapture;
        _monitors = monitors;
    }

    public IReadOnlyList<WindowCaptureTarget> ListCapturableWindows()
    {
        var result = new List<WindowCaptureTarget>();
        var currentProcessId = (uint)Environment.ProcessId;
        var virtualBounds = _monitors.GetVirtualScreenBounds();
        var nextZOrder = 0;
        NativeMethods.EnumWindows((hwnd, ignored) =>
        {
            if (result.Count >= MaximumCatalogWindows) return false;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd)) return true;
            var titleLength = NativeMethods.GetWindowTextLength(hwnd);
            if (titleLength <= 0) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var native)) return true;
            var bounds = new PixelRect(native.Left, native.Top, Math.Max(0, native.Right - native.Left), Math.Max(0, native.Bottom - native.Top));
            if (bounds.Width < 32 || bounds.Height < 32 || bounds.Intersect(virtualBounds).IsEmpty) return true;

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == currentProcessId) return true;
            var titleBuilder = new StringBuilder(Math.Clamp(titleLength + 1, 2, 2048));
            _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title)) return true;
            var processName = TryGetProcessName(processId);
            var executablePath = TryGetExecutablePath(processId);
            result.Add(new WindowCaptureTarget(hwnd, bounds, title, processName, executablePath, TryGetClassName(hwnd), processId, nextZOrder++));
            return true;
        }, IntPtr.Zero);

        return result
            .OrderBy(target => target.ProcessName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCatalogWindows)
            .ToArray();
    }

    public WindowCaptureTarget? TryGetForegroundTarget()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        return ListCapturableWindows().FirstOrDefault(target => target.Handle == hwnd);
    }

    public CaptureAsset CaptureForegroundWindow(bool includeCursor = false)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("No capturable foreground window is available.");
        return CaptureWindow(hwnd, includeCursor);
    }

    public CaptureAsset CaptureWindow(WindowCaptureTarget target, bool includeCursor = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureWindow(target.Handle, includeCursor, target.ProcessName, target.ExecutablePath);
    }

    private CaptureAsset CaptureWindow(IntPtr hwnd, bool includeCursor, string? knownProcessName = null, string? knownExecutablePath = null)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var native))
            throw new InvalidOperationException("The selected window is no longer available.");

        var requested = new PixelRect(native.Left, native.Top, Math.Max(0, native.Right - native.Left), Math.Max(0, native.Bottom - native.Top));
        var visible = requested.Intersect(_monitors.GetVirtualScreenBounds());
        if (visible.IsEmpty) throw new InvalidOperationException("The selected window is outside the visible virtual desktop.");

        var titleLength = Math.Clamp(NativeMethods.GetWindowTextLength(hwnd) + 1, 2, 2048);
        var titleBuilder = new StringBuilder(titleLength);
        _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        var title = string.IsNullOrWhiteSpace(titleBuilder.ToString()) ? "Window" : titleBuilder.ToString().Trim();
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var processName = knownProcessName ?? TryGetProcessName(processId);
        var executablePath = knownExecutablePath ?? TryGetExecutablePath(processId);
        return _screenCapture.Capture(
            visible,
            CaptureSourceKind.Window,
            title,
            includeCursor,
            windowTitle: title,
            processName: processName,
            executablePath: executablePath,
            windowHandle: hwnd,
            sourceBounds: visible,
            targetKind: Magic.Capture.Core.Capture.CaptureTargetKind.Window);
    }

    private static string? TryGetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(512);
        return NativeMethods.GetClassName(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString().Trim()
            : null;
    }

    private static string? TryGetExecutablePath(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim();
            return path.Length <= 2048 ? path : path[..2048];
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (NotSupportedException) { return null; }
        catch (OverflowException) { return null; }
    }

    private static string? TryGetProcessName(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return string.IsNullOrWhiteSpace(process.ProcessName) ? null : process.ProcessName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (OverflowException) { return null; }
    }
}
