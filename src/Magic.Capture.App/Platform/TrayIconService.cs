using System.Runtime.InteropServices;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Commerce;

namespace Magic.Capture.App.Platform;

internal sealed class TrayIconService : IDisposable
{
    private const uint TrayId = 1;
    private const uint CmdRegion = 1001;
    private const uint CmdRepeat = 1002;
    private const uint CmdMonitor = 1003;
    private const uint CmdVirtual = 1004;
    private const uint CmdWindow = 1005;
    private const uint CmdOpen = 1006;
    private const uint CmdHistory = 1007;
    private const uint CmdSettings = 1008;
    private const uint CmdPlan = 1009;
    private const uint CmdRestorePins = 1010;
    private const uint CmdExit = 1011;

    private readonly IntPtr _windowHandle;
    private readonly NativeMessageRouter _router;
    private NotifyIconData _data;
    private bool _added;
    private ProductTier _tier = ProductTier.Free;

    public TrayIconService(IntPtr windowHandle, NativeMessageRouter router)
    {
        _windowHandle = windowHandle;
        _router = router;
        _router.MessageReceived += OnMessage;
    }

    public event EventHandler? RegionCaptureRequested;
    public event EventHandler? RepeatRegionRequested;
    public event EventHandler? MonitorCaptureRequested;
    public event EventHandler? VirtualDesktopCaptureRequested;
    public event EventHandler? WindowCaptureRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? PlanRequested;
    public event EventHandler? RestorePinsRequested;
    public event EventHandler? ExitRequested;

    public void SetTier(ProductTier tier)
    {
        _tier = tier;
        if (!_added) return;
        _data.Tip = tier switch
        {
            ProductTier.ProLifetime => "Magic Capture Desktop — Pro",
            ProductTier.PlusTrial => "Magic Capture Desktop — Plus trial",
            _ => "Magic Capture Desktop — Free"
        };
        NativeMethods.ShellNotifyIcon(NativeConstants.NimModify, ref _data);
    }

    public void Add()
    {
        if (_added) return;
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = TrayId,
            Flags = NativeConstants.NifMessage | NativeConstants.NifIcon | NativeConstants.NifTip,
            CallbackMessage = NativeConstants.TrayCallbackMessage,
            IconHandle = NativeMethods.LoadIcon(IntPtr.Zero, NativeConstants.IdiApplication),
            Tip = "Magic Capture Desktop",
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        _added = NativeMethods.ShellNotifyIcon(NativeConstants.NimAdd, ref _data);
    }

    private void OnMessage(object? sender, NativeWindowMessage message)
    {
        if (message.Message != NativeConstants.TrayCallbackMessage) return;
        var mouseMessage = unchecked((uint)message.LParam.ToInt64());
        if (mouseMessage == NativeConstants.WmLButtonUp)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (mouseMessage == NativeConstants.WmRButtonUp) ShowContextMenu();
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdRegion, "Capture region    Win+Shift+X");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdRepeat, _tier == ProductTier.ProLifetime ? "Repeat last region    Win+Shift+R" : "Repeat last region    PRO");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdMonitor, "Capture active monitor");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdWindow, "Capture foreground window");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdVirtual, "Capture virtual desktop");
            NativeMethods.AppendMenu(menu, NativeConstants.MfSeparator, UIntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdOpen, "Open Magic Capture Desktop");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdHistory, "History");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdSettings, "Settings");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdPlan, $"Plan: {TierLabel(_tier)}");
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdRestorePins, "Make pins interactive");
            NativeMethods.AppendMenu(menu, NativeConstants.MfSeparator, UIntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeConstants.MfString, CmdExit, "Exit Magic Capture Desktop");
            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_windowHandle);
            var command = NativeMethods.TrackPopupMenuEx(menu, NativeConstants.TpmRightButton | NativeConstants.TpmReturnCmd, point.X, point.Y, _windowHandle, IntPtr.Zero);
            Dispatch(command);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static string TierLabel(ProductTier tier) => tier switch
    {
        ProductTier.ProLifetime => "Pro Lifetime",
        ProductTier.PlusTrial => "Plus Trial",
        _ => "Free"
    };

    private void Dispatch(uint command)
    {
        switch (command)
        {
            case CmdRegion: RegionCaptureRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdRepeat: RepeatRegionRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdMonitor: MonitorCaptureRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdVirtual: VirtualDesktopCaptureRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdWindow: WindowCaptureRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdOpen: OpenRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdHistory: HistoryRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdSettings: SettingsRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdPlan: PlanRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdRestorePins: RestorePinsRequested?.Invoke(this, EventArgs.Empty); break;
            case CmdExit: ExitRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }

    public void Dispose()
    {
        _router.MessageReceived -= OnMessage;
        if (_added)
        {
            NativeMethods.ShellNotifyIcon(NativeConstants.NimDelete, ref _data);
            _added = false;
        }
    }
}
