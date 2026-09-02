using System.Runtime.InteropServices;

namespace Magic.Capture.App.Platform.Native;

internal static class UiAutomationInterop
{
    internal const int TreeScopeElementAndChildren = 0x1 | 0x2;
    internal const int AutomationElementModeFull = 1;

    internal const int RuntimeIdProperty = 30000;
    internal const int BoundingRectangleProperty = 30001;
    internal const int ProcessIdProperty = 30002;
    internal const int ControlTypeProperty = 30003;
    internal const int LocalizedControlTypeProperty = 30004;
    internal const int NameProperty = 30005;
    internal const int AcceleratorKeyProperty = 30006;
    internal const int AccessKeyProperty = 30007;
    internal const int HasKeyboardFocusProperty = 30008;
    internal const int IsEnabledProperty = 30010;
    internal const int AutomationIdProperty = 30011;
    internal const int IsPasswordProperty = 30019;
    internal const int IsOffscreenProperty = 30022;
    internal const int ValueValueProperty = 30045;
    internal const int SelectionItemIsSelectedProperty = 30079;
    internal const int ToggleToggleStateProperty = 30086;

    private static readonly Guid ClsidCuiAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IidUiAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    internal static IUiAutomationNative CreateAutomation()
    {
        var clsid = ClsidCuiAutomation;
        var iid = IidUiAutomation;
        var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        if (pointer == IntPtr.Zero) throw new COMException("CUIAutomation returned a null interface pointer.");
        try
        {
            return (IUiAutomationNative)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            _ = Marshal.Release(pointer);
        }
    }

    internal static bool TryInitializeMta(out bool uninitialize)
    {
        var hr = CoInitializeEx(IntPtr.Zero, 0x0); // COINIT_MULTITHREADED
        uninitialize = hr >= 0;
        return hr >= 0;
    }

    internal static void Uninitialize() => CoUninitialize();

    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { _ = Marshal.ReleaseComObject(value); }
            catch (ArgumentException) { }
            catch (InvalidComObjectException) { }
        }
    }

    internal static void ReleaseUnknown(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return;
        try { _ = Marshal.Release(pointer); }
        catch (ArgumentException) { }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}

[ComImport]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUiAutomationNative
{
    [PreserveSig] int CompareElements(IntPtr element1, IntPtr element2, out int areSame);
    [PreserveSig] int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, out int areSame);
    [PreserveSig] int GetRootElement(out IntPtr root);
    [PreserveSig] int ElementFromHandle(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] out IUiAutomationElementNative? element);
    [PreserveSig] int ElementFromPoint(long point, out IntPtr element);
    [PreserveSig] int GetFocusedElement(out IntPtr element);
    [PreserveSig] int GetRootElementBuildCache(IntPtr cacheRequest, out IntPtr root);
    [PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest, out IntPtr element);
    [PreserveSig] int ElementFromPointBuildCache(long point, IntPtr cacheRequest, out IntPtr element);
    [PreserveSig] int GetFocusedElementBuildCache(IntPtr cacheRequest, out IntPtr element);
    [PreserveSig] int CreateTreeWalker(IntPtr condition, out IntPtr walker);
    [PreserveSig] int GetControlViewWalker(out IntPtr walker);
    [PreserveSig] int GetContentViewWalker(out IntPtr walker);
    [PreserveSig] int GetRawViewWalker(out IntPtr walker);
    [PreserveSig] int GetRawViewCondition(out IntPtr condition);
    [PreserveSig] int GetControlViewCondition(out IntPtr condition);
    [PreserveSig] int GetContentViewCondition(out IntPtr condition);
    [PreserveSig] int CreateCacheRequest([MarshalAs(UnmanagedType.Interface)] out IUiAutomationCacheRequestNative? request);
}

[ComImport]
[Guid("B32A92B5-BC25-4078-9C08-D7EE95C48E03")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUiAutomationCacheRequestNative
{
    [PreserveSig] int AddProperty(int propertyId);
    [PreserveSig] int AddPattern(int patternId);
    [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IUiAutomationCacheRequestNative? clonedRequest);
    [PreserveSig] int GetTreeScope(out int scope);
    [PreserveSig] int SetTreeScope(int scope);
    [PreserveSig] int GetTreeFilter(out IntPtr filter);
    [PreserveSig] int SetTreeFilter(IntPtr filter);
    [PreserveSig] int GetAutomationElementMode(out int mode);
    [PreserveSig] int SetAutomationElementMode(int mode);
}

[ComImport]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUiAutomationElementNative
{
    [PreserveSig] int SetFocus();
    [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
    [PreserveSig] int FindFirst(int scope, IntPtr condition, out IntPtr found);
    [PreserveSig] int FindAll(int scope, IntPtr condition, out IntPtr found);
    [PreserveSig] int FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);
    [PreserveSig] int FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);
    [PreserveSig] int BuildUpdatedCache([MarshalAs(UnmanagedType.Interface)] IUiAutomationCacheRequestNative cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUiAutomationElementNative? updatedElement);
    [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    [PreserveSig] int GetCurrentPropertyValueEx(int propertyId, int ignoreDefaultValue, [MarshalAs(UnmanagedType.Struct)] out object? value);
    [PreserveSig] int GetCachedPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    [PreserveSig] int GetCachedPropertyValueEx(int propertyId, int ignoreDefaultValue, [MarshalAs(UnmanagedType.Struct)] out object? value);
    [PreserveSig] int GetCurrentPatternAs(int patternId, ref Guid riid, out IntPtr patternObject);
    [PreserveSig] int GetCachedPatternAs(int patternId, ref Guid riid, out IntPtr patternObject);
    [PreserveSig] int GetCurrentPattern(int patternId, out IntPtr patternObject);
    [PreserveSig] int GetCachedPattern(int patternId, out IntPtr patternObject);
    [PreserveSig] int GetCachedParent([MarshalAs(UnmanagedType.Interface)] out IUiAutomationElementNative? parent);
    [PreserveSig] int GetCachedChildren([MarshalAs(UnmanagedType.Interface)] out IUiAutomationElementArrayNative? children);
}

[ComImport]
[Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUiAutomationElementArrayNative
{
    [PreserveSig] int GetLength(out int length);
    [PreserveSig] int GetElement(int index, [MarshalAs(UnmanagedType.Interface)] out IUiAutomationElementNative? element);
}
