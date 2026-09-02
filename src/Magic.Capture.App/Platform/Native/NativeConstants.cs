namespace Magic.Capture.App.Platform.Native;

internal static class NativeConstants
{
    public const uint WmHotkey = 0x0312;
    public const uint WmClipboardUpdate = 0x031D;
    public const uint EventSystemForeground = 0x0003;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint WinEventSkipOwnProcess = 0x0002;
    public const uint WmApp = 0x8000;
    public const uint TrayCallbackMessage = WmApp + 42;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLButtonDown = 0x0201;
    public const uint WmLButtonUp = 0x0202;
    public const uint WmRButtonDown = 0x0204;
    public const uint WmRButtonUp = 0x0205;
    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmSysKeyDown = 0x0104;
    public const uint WmSysKeyUp = 0x0105;
    public const uint WmSizing = 0x0214;
    public const uint WmMoving = 0x0216;


    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const uint VkShift = 0x10;
    public const uint VkControl = 0x11;
    public const uint VkMenu = 0x12;
    public const uint VkLWin = 0x5B;
    public const uint VkRWin = 0x5C;
    public const uint VkLShift = 0xA0;
    public const uint VkRShift = 0xA1;
    public const uint VkLControl = 0xA2;
    public const uint VkRControl = 0xA3;
    public const uint VkLMenu = 0xA4;
    public const uint VkRMenu = 0xA5;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public const uint MonitorDefaultToNearest = 0x00000002;
    public const uint MonitorInfoPrimary = 0x00000001;
    public const int MdtEffectiveDpi = 0;
    public const uint CursorShowing = 0x00000001;
    public const uint DiNormal = 0x0003;
    public const uint InputMouse = 0;
    public const uint MouseEventWheel = 0x0800;
    public const uint MouseEventHWheel = 0x01000;
    public const int WheelDelta = 120;

    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    public const uint NimAdd = 0x00000000;
    public const uint NimModify = 0x00000001;
    public const uint NimDelete = 0x00000002;
    public const uint NifMessage = 0x00000001;
    public const uint NifIcon = 0x00000002;
    public const uint NifTip = 0x00000004;

    public const uint MfString = 0x00000000;
    public const uint MfSeparator = 0x00000800;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmReturnCmd = 0x0100;

    public const int GwlExStyle = -20;
    public const long WsExLayered = 0x00080000L;
    public const long WsExTransparent = 0x00000020L;
    public const uint LwaAlpha = 0x00000002;

    public const uint WdaNone = 0x00000000;
    public const uint WdaExcludeFromCapture = 0x00000011;

    public static readonly IntPtr IdiApplication = new(32512);
}
