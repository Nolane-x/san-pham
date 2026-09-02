using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Magic.Capture.App.Capture;

/// <summary>Win32 interop for GraphicsCaptureItem on the Windows 10 1903 API floor.</summary>
internal static unsafe class GraphicsCaptureItemInterop
{
    private const string RuntimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid InteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr windowHandle) => Create(windowHandle, vtableSlot: 3);
    public static GraphicsCaptureItem CreateForMonitor(IntPtr monitorHandle) => Create(monitorHandle, vtableSlot: 4);

    private static GraphicsCaptureItem Create(IntPtr handle, int vtableSlot)
    {
        if (handle == IntPtr.Zero) throw new ArgumentException("A native capture handle is required.", nameof(handle));
        WindowsCreateString(RuntimeClassName, RuntimeClassName.Length, out var className).ThrowIfFailed();
        try
        {
            var iid = InteropIid;
            RoGetActivationFactory(className, in iid, out var factory).ThrowIfFailed();
            try
            {
                var itemIid = ItemIid;
                IntPtr item = IntPtr.Zero;
                var vtable = *(IntPtr**)factory;
                var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[vtableSlot];
                var hr = create(factory, handle, &itemIid, &item);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    return GraphicsCaptureItem.FromAbi(item)
                        ?? throw new InvalidOperationException("GraphicsCaptureItem projection returned null.");
                }
                finally
                {
                    if (item != IntPtr.Zero) Marshal.Release(item);
                }
            }
            finally
            {
                if (factory != IntPtr.Zero) Marshal.Release(factory);
            }
        }
        finally
        {
            if (className != IntPtr.Zero) WindowsDeleteString(className);
        }
    }

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    private static void ThrowIfFailed(this int hr) => Marshal.ThrowExceptionForHR(hr);
}
