using System.Runtime.InteropServices;

namespace Magic.Capture.App.Platform.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}


[StructLayout(LayoutKind.Sequential)]
internal struct CursorInfo
{
    public int Size;
    public uint Flags;
    public IntPtr CursorHandle;
    public NativePoint ScreenPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IconInfo
{
    [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
    public uint HotspotX;
    public uint HotspotY;
    public IntPtr MaskBitmap;
    public IntPtr ColorBitmap;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect WorkArea;
    public uint Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NotifyIconData
{
    public uint Size;
    public IntPtr WindowHandle;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr IconHandle;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Tip;

    public uint State;
    public uint StateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Info;

    public uint VersionOrTimeout;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string InfoTitle;

    public uint InfoFlags;
    public Guid GuidItem;
    public IntPtr BalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public uint Type;
    public NativeMouseInput Mouse;
}


[StructLayout(LayoutKind.Sequential)]
internal struct LowLevelMouseHookStruct
{
    public NativePoint Point;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LowLevelKeyboardHookStruct
{
    public uint VirtualKey;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}
