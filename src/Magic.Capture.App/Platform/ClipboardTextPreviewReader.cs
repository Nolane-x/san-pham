using System.Runtime.InteropServices;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.App.Platform;

internal static class ClipboardTextPreviewReader
{
    private const uint CfUnicodeText = 13;

    public static bool TryRead(out string preview, out bool truncated)
    {
        preview = string.Empty;
        truncated = false;
        if (!OpenClipboard(IntPtr.Zero)) return false;

        IntPtr handle = IntPtr.Zero;
        IntPtr pointer = IntPtr.Zero;
        try
        {
            handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return false;

            var byteLength = GlobalSize(handle);
            var maximumCharacters = ClipboardPreviewPolicy.BoundedCharacterCount(byteLength);
            if (maximumCharacters <= 0) return true;

            pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return false;

            var actualCharacters = 0;
            while (actualCharacters < maximumCharacters && Marshal.ReadInt16(pointer, actualCharacters * sizeof(char)) != 0)
                actualCharacters++;

            preview = actualCharacters == 0 ? string.Empty : Marshal.PtrToStringUni(pointer, actualCharacters) ?? string.Empty;
            var availableCharacters = byteLength / sizeof(char);
            truncated = actualCharacters == ClipboardPreviewPolicy.MaximumTextPreviewCharacters
                && availableCharacters > (nuint)actualCharacters;
            return true;
        }
        finally
        {
            if (pointer != IntPtr.Zero) GlobalUnlock(handle);
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(IntPtr hMem);
}
