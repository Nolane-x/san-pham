using System.ComponentModel;
using System.Runtime.InteropServices;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Documentation;

internal sealed record StepRecorderInputAction(
    PixelPoint DesktopPoint,
    DocumentationMouseButton? MouseButton,
    string? SafeKeyGesture,
    DateTimeOffset TimestampUtc)
{
    public bool IsClick => MouseButton is not null;
}

internal sealed class StepRecorderInputTracker : IDisposable
{
    private const uint WmMButtonDown = 0x0207;
    private readonly NativeMethods.LowLevelHookProc _mouseProc;
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;
    private readonly object _gate = new();
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private DocumentationClickEvent? _lastClick;
    private bool _control;
    private bool _alt;
    private bool _shift;
    private bool _win;
    private bool _disposed;

    public StepRecorderInputTracker()
    {
        _mouseProc = MouseHook;
        _keyboardProc = KeyboardHook;
    }

    public event EventHandler<StepRecorderInputAction>? ActionCaptured;

    public bool IsRunning => _mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero;

    public void Start()
    {
        ThrowIfDisposed();
        if (IsRunning) return;

        _mouseHook = NativeMethods.SetWindowsHookExW(NativeConstants.WhMouseLl, _mouseProc, IntPtr.Zero, 0);
        if (_mouseHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the Step Recorder mouse hook.");

        _keyboardHook = NativeMethods.SetWindowsHookExW(NativeConstants.WhKeyboardLl, _keyboardProc, IntPtr.Zero, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            throw new Win32Exception(error, "Unable to install the Step Recorder keyboard hook.");
        }
    }

    private IntPtr MouseHook(int code, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var message = unchecked((uint)wParam.ToUInt64());
                DocumentationMouseButton? button = message switch
                {
                    NativeConstants.WmLButtonDown => DocumentationMouseButton.Left,
                    NativeConstants.WmRButtonDown => DocumentationMouseButton.Right,
                    WmMButtonDown => DocumentationMouseButton.Middle,
                    _ => null
                };
                if (button is { } pressed)
                {
                    var data = Marshal.PtrToStructure<LowLevelMouseHookStruct>(lParam);
                    var click = new DocumentationClickEvent(
                        new PixelPoint(data.Point.X, data.Point.Y),
                        pressed,
                        DateTimeOffset.UtcNow);
                    var emit = false;
                    lock (_gate)
                    {
                        if (_lastClick is null || !DocumentationPolicy.ShouldCoalesce(_lastClick, click))
                        {
                            _lastClick = click;
                            emit = true;
                        }
                    }
                    if (emit)
                        ActionCaptured?.Invoke(this, new StepRecorderInputAction(click.DesktopPoint, click.Button, null, click.TimestampUtc));
                }
            }
        }
        catch (Exception) when (!_disposed)
        {
            // Step Recorder is observational only. Hook failures must never interrupt the system input chain.
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardHook(int code, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var message = unchecked((uint)wParam.ToUInt64());
                var data = Marshal.PtrToStructure<LowLevelKeyboardHookStruct>(lParam);
                var down = message is NativeConstants.WmKeyDown or NativeConstants.WmSysKeyDown;
                lock (_gate)
                {
                    UpdateModifier(data.VirtualKey, down);
                    if (down)
                    {
                        var label = RecordingSafeKeyFormatter.Format(data.VirtualKey, _control, _alt, _shift, _win);
                        if (DocumentationPolicy.IsSafeKeyboardGesture(label) && NativeMethods.GetCursorPos(out var cursor))
                        {
                            ActionCaptured?.Invoke(this, new StepRecorderInputAction(
                                new PixelPoint(cursor.X, cursor.Y),
                                null,
                                label,
                                DateTimeOffset.UtcNow));
                        }
                    }
                }
            }
        }
        catch (Exception) when (!_disposed)
        {
            // No typed text is buffered and processing failures never break the keyboard chain.
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void UpdateModifier(uint key, bool down)
    {
        if (key is NativeConstants.VkControl or NativeConstants.VkLControl or NativeConstants.VkRControl) _control = down;
        else if (key is NativeConstants.VkMenu or NativeConstants.VkLMenu or NativeConstants.VkRMenu) _alt = down;
        else if (key is NativeConstants.VkShift or NativeConstants.VkLShift or NativeConstants.VkRShift) _shift = down;
        else if (key is NativeConstants.VkLWin or NativeConstants.VkRWin) _win = down;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        ActionCaptured = null;
    }
}
