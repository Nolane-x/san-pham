using Magic.Capture.App.Platform.Native;

namespace Magic.Capture.App.Platform;

internal sealed class NativeMessageRouter : IDisposable
{
    private readonly NativeMethods.SubclassProc _callback;
    private IntPtr _windowHandle;
    private bool _attached;

    public NativeMessageRouter() => _callback = WindowSubclassProc;

    public event EventHandler<NativeWindowMessage>? MessageReceived;

    public void Attach(IntPtr windowHandle)
    {
        if (_attached) return;
        _windowHandle = windowHandle;
        if (!NativeMethods.SetWindowSubclass(windowHandle, _callback, UIntPtr.Zero, UIntPtr.Zero))
            throw new InvalidOperationException("Unable to install the native window message router.");
        _attached = true;
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData)
    {
        MessageReceived?.Invoke(this, new NativeWindowMessage(message, wParam, lParam));
        return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_attached)
        {
            NativeMethods.RemoveWindowSubclass(_windowHandle, _callback, UIntPtr.Zero);
            _attached = false;
        }
    }
}

internal sealed class NativeWindowMessage(uint message, UIntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;
    public UIntPtr WParam { get; } = wParam;
    public IntPtr LParam { get; } = lParam;
}
