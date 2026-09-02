using System.ComponentModel;
using Magic.Capture.App.Platform.Native;

namespace Magic.Capture.App.Recording;

internal static class RecordingControlCaptureExclusion
{
    public static void Exclude(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) throw new ArgumentException("A top-level window handle is required.", nameof(windowHandle));
        if (!NativeMethods.SetWindowDisplayAffinity(windowHandle, NativeConstants.WdaExcludeFromCapture))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Windows could not exclude the recording controls from capture.");
    }

    public static void Restore(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;
        if (!NativeMethods.SetWindowDisplayAffinity(windowHandle, NativeConstants.WdaNone))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Windows could not restore normal capture affinity for the recording controls.");
    }
}
