using Magic.Capture.App.Platform.Native;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Magic.Capture.App.Platform;

internal static class WindowHelpers
{
    public static IntPtr GetWindowHandle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window)
    {
        var hwnd = GetWindowHandle(window);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(id);
    }

    public static void MoveAndResize(Window window, int x, int y, int width, int height) =>
        GetAppWindow(window).MoveAndResize(new RectInt32(x, y, width, height));

    public static void MakeBorderlessTopmost(Window window)
    {
        if (GetAppWindow(window).Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }
    }

    public static void SetAlwaysOnTop(Window window, bool enabled)
    {
        if (GetAppWindow(window).Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = enabled;
    }

    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = GetWindowHandle(window);
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GwlExStyle).ToInt64();
        style = enabled ? style | NativeConstants.WsExTransparent : style & ~NativeConstants.WsExTransparent;
        NativeMethods.SetWindowLongPtr(hwnd, NativeConstants.GwlExStyle, new IntPtr(style));
    }

    public static void SetOpacity(Window window, double opacity)
    {
        opacity = Math.Clamp(opacity, 0.1, 1.0);
        var hwnd = GetWindowHandle(window);
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeConstants.GwlExStyle, new IntPtr(style | NativeConstants.WsExLayered));
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(opacity * 255), NativeConstants.LwaAlpha);
    }
}
