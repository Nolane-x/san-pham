using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.App.Capture;

internal sealed class UiAutomationSnapshotService
{
    private const int MaximumForegroundWaitMilliseconds = 260;
    private const int MaximumTraversalMilliseconds = 900;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);

    public async Task<UiAutomationSnapshot> CaptureForMonitorAsync(
        PixelRect monitorBounds,
        IReadOnlyList<WindowCaptureTarget> windowCatalog)
    {
        if (monitorBounds.IsEmpty || windowCatalog.Count == 0) return UiAutomationSnapshot.Empty;
        if (!await _snapshotGate.WaitAsync(0).ConfigureAwait(false))
            return new UiAutomationSnapshot(DateTimeOffset.UtcNow, [], true, "UI Automation snapshot is already in progress.");

        var windows = windowCatalog
            .Where(window => !window.Bounds.Intersect(monitorBounds).IsEmpty)
            .OrderBy(window => window.ZOrder)
            .Take(UiAutomationSnapshotRules.MaximumTopLevelWindows)
            .ToArray();
        if (windows.Length == 0)
        {
            _snapshotGate.Release();
            return UiAutomationSnapshot.Empty;
        }

        var worker = RunMtaAsync(() => CaptureCore(monitorBounds, windows));
        _ = worker.ContinueWith(
            completedWorker =>
            {
                // Observe faults even when the foreground latency budget returned first; otherwise a
                // provider failure on the detached worker can surface as an unobserved task exception.
                _ = completedWorker.Exception;
                _snapshotGate.Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var completed = await Task.WhenAny(worker, Task.Delay(MaximumForegroundWaitMilliseconds)).ConfigureAwait(false);
        if (completed != worker)
            return new UiAutomationSnapshot(DateTimeOffset.UtcNow, [], true, "UI Automation snapshot exceeded the capture-start latency budget; window snapping remains available.");

        return await worker.ConfigureAwait(false);
    }

    private static UiAutomationSnapshot CaptureCore(PixelRect monitorBounds, IReadOnlyList<WindowCaptureTarget> windows)
    {
        var stopwatch = Stopwatch.StartNew();
        var nodes = new List<UiAutomationSnapshotNode>(UiAutomationSnapshotRules.MaximumNodes);
        var processNames = new Dictionary<int, string?>();
        var truncated = false;
        var providerFailures = 0;
        var sequence = 0;
        IUiAutomationNative? automation = null;
        IUiAutomationCacheRequestNative? cacheRequest = null;
        var uninitialize = false;

        try
        {
            if (!UiAutomationInterop.TryInitializeMta(out uninitialize))
                return new UiAutomationSnapshot(DateTimeOffset.UtcNow, [], true, "Unable to initialize the UI Automation MTA worker.");

            automation = UiAutomationInterop.CreateAutomation();
            ThrowIfFailed(automation.CreateCacheRequest(out cacheRequest));
            if (cacheRequest is null) throw new COMException("UI Automation returned a null cache request.");
            ConfigureCache(automation, cacheRequest);

            foreach (var window in windows)
            {
                if (nodes.Count >= UiAutomationSnapshotRules.MaximumNodes || stopwatch.ElapsedMilliseconds >= MaximumTraversalMilliseconds)
                {
                    truncated = true;
                    break;
                }

                IUiAutomationElementNative? root = null;
                try
                {
                    ThrowIfFailed(automation.ElementFromHandle(window.Handle, out root));
                    if (root is null) continue;
                    WalkElement(root, null, 0, window, monitorBounds, cacheRequest, stopwatch, nodes, processNames, ref sequence, ref truncated, ref providerFailures);
                }
                catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex) && IsRecoverableProviderFailure(ex))
                {
                    providerFailures++;
                }
                finally
                {
                    UiAutomationInterop.Release(root);
                }
            }
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex) && IsRecoverableProviderFailure(ex))
        {
            providerFailures++;
            truncated = true;
        }
        finally
        {
            UiAutomationInterop.Release(cacheRequest);
            UiAutomationInterop.Release(automation);
            if (uninitialize) UiAutomationInterop.Uninitialize();
        }

        var diagnostic = providerFailures == 0
            ? null
            : $"{providerFailures.ToString(CultureInfo.InvariantCulture)} UI Automation provider call(s) were unavailable.";
        return UiAutomationSnapshotRules.Normalize(nodes, truncated, diagnostic);
    }

    private static void WalkElement(
        IUiAutomationElementNative liveElement,
        string? parentStableKey,
        int depth,
        WindowCaptureTarget window,
        PixelRect monitorBounds,
        IUiAutomationCacheRequestNative cacheRequest,
        Stopwatch stopwatch,
        List<UiAutomationSnapshotNode> output,
        Dictionary<int, string?> processNames,
        ref int sequence,
        ref bool truncated,
        ref int providerFailures)
    {
        if (depth > UiAutomationSnapshotRules.MaximumDepth || output.Count >= UiAutomationSnapshotRules.MaximumNodes || stopwatch.ElapsedMilliseconds >= MaximumTraversalMilliseconds)
        {
            truncated = true;
            return;
        }

        IUiAutomationElementNative? cached = null;
        IUiAutomationElementArrayNative? children = null;
        try
        {
            ThrowIfFailed(liveElement.BuildUpdatedCache(cacheRequest, out cached));
            if (cached is null) return;

            var node = ReadCachedNode(cached, parentStableKey, depth, window, processNames, ref sequence);
            if (node is null || node.DesktopBounds.Intersect(monitorBounds).IsEmpty) return;
            output.Add(node);
            if (output.Count >= UiAutomationSnapshotRules.MaximumNodes)
            {
                truncated = true;
                return;
            }

            if (depth >= UiAutomationSnapshotRules.MaximumDepth) return;
            var hr = cached.GetCachedChildren(out children);
            if (hr < 0 || children is null) return;
            ThrowIfFailed(children.GetLength(out var count));
            count = Math.Clamp(count, 0, UiAutomationSnapshotRules.MaximumNodes - output.Count);
            for (var index = 0; index < count; index++)
            {
                if (output.Count >= UiAutomationSnapshotRules.MaximumNodes || stopwatch.ElapsedMilliseconds >= MaximumTraversalMilliseconds)
                {
                    truncated = true;
                    break;
                }

                IUiAutomationElementNative? child = null;
                try
                {
                    ThrowIfFailed(children.GetElement(index, out child));
                    if (child is null) continue;
                    WalkElement(child, node.StableKey, depth + 1, window, monitorBounds, cacheRequest, stopwatch, output, processNames, ref sequence, ref truncated, ref providerFailures);
                }
                catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex) && IsRecoverableProviderFailure(ex))
                {
                    providerFailures++;
                }
                finally
                {
                    UiAutomationInterop.Release(child);
                }
            }
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex) && IsRecoverableProviderFailure(ex))
        {
            providerFailures++;
        }
        finally
        {
            UiAutomationInterop.Release(children);
            UiAutomationInterop.Release(cached);
        }
    }

    private static UiAutomationSnapshotNode? ReadCachedNode(
        IUiAutomationElementNative element,
        string? parentStableKey,
        int depth,
        WindowCaptureTarget window,
        Dictionary<int, string?> processNames,
        ref int sequence)
    {
        if (GetBool(element, UiAutomationInterop.IsOffscreenProperty) == true) return null;
        var bounds = GetRect(element, UiAutomationInterop.BoundingRectangleProperty);
        if (bounds.IsEmpty) return null;

        var processId = GetInt(element, UiAutomationInterop.ProcessIdProperty) ?? checked((int)window.ProcessId);
        var isPassword = GetBool(element, UiAutomationInterop.IsPasswordProperty);
        var runtimeId = GetIntArray(element, UiAutomationInterop.RuntimeIdProperty);
        var automationId = GetString(element, UiAutomationInterop.AutomationIdProperty);
        var controlTypeId = GetInt(element, UiAutomationInterop.ControlTypeProperty);
        var localizedType = GetString(element, UiAutomationInterop.LocalizedControlTypeProperty);
        var controlType = ControlTypeName(controlTypeId, localizedType);
        var stableKey = BuildStableKey(processId, runtimeId, window.ZOrder, depth, bounds, automationId, controlType, sequence++);

        var toggleState = GetInt(element, UiAutomationInterop.ToggleToggleStateProperty);
        bool? isChecked = toggleState switch { 0 => false, 1 => true, _ => null };
        var processName = processId == checked((int)window.ProcessId)
            ? window.ProcessName
            : GetProcessName(processId, processNames);

        return new UiAutomationSnapshotNode(
            stableKey,
            parentStableKey,
            controlType,
            GetString(element, UiAutomationInterop.NameProperty),
            automationId,
            isPassword == true ? null : GetString(element, UiAutomationInterop.ValueValueProperty),
            GetBool(element, UiAutomationInterop.IsEnabledProperty),
            isChecked,
            GetBool(element, UiAutomationInterop.SelectionItemIsSelectedProperty),
            GetBool(element, UiAutomationInterop.HasKeyboardFocusProperty),
            bounds,
            GetString(element, UiAutomationInterop.AccessKeyProperty),
            GetString(element, UiAutomationInterop.AcceleratorKeyProperty),
            processId,
            processName,
            window.Title,
            window.ZOrder,
            depth,
            isPassword);
    }

    private static void ConfigureCache(IUiAutomationNative automation, IUiAutomationCacheRequestNative request)
    {
        foreach (var property in new[]
        {
            UiAutomationInterop.RuntimeIdProperty,
            UiAutomationInterop.BoundingRectangleProperty,
            UiAutomationInterop.ProcessIdProperty,
            UiAutomationInterop.ControlTypeProperty,
            UiAutomationInterop.LocalizedControlTypeProperty,
            UiAutomationInterop.NameProperty,
            UiAutomationInterop.AcceleratorKeyProperty,
            UiAutomationInterop.AccessKeyProperty,
            UiAutomationInterop.HasKeyboardFocusProperty,
            UiAutomationInterop.IsEnabledProperty,
            UiAutomationInterop.AutomationIdProperty,
            UiAutomationInterop.IsPasswordProperty,
            UiAutomationInterop.IsOffscreenProperty,
            UiAutomationInterop.ValueValueProperty,
            UiAutomationInterop.SelectionItemIsSelectedProperty,
            UiAutomationInterop.ToggleToggleStateProperty
        }) ThrowIfFailed(request.AddProperty(property));

        ThrowIfFailed(request.SetTreeScope(UiAutomationInterop.TreeScopeElementAndChildren));
        ThrowIfFailed(request.SetAutomationElementMode(UiAutomationInterop.AutomationElementModeFull));

        IntPtr controlViewCondition = IntPtr.Zero;
        try
        {
            ThrowIfFailed(automation.GetControlViewCondition(out controlViewCondition));
            if (controlViewCondition == IntPtr.Zero) throw new COMException("UI Automation returned a null Control View condition.");
            ThrowIfFailed(request.SetTreeFilter(controlViewCondition));
        }
        finally
        {
            UiAutomationInterop.ReleaseUnknown(controlViewCondition);
        }
    }

    private static string BuildStableKey(int processId, int[]? runtimeId, int zOrder, int depth, PixelRect bounds, string? automationId, string controlType, int sequence)
    {
        if (runtimeId is { Length: > 0 and <= 64 })
            return $"p{processId}:r{string.Join('.', runtimeId)}";
        return $"p{processId}:w{zOrder}:d{depth}:{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}:{automationId ?? controlType}:{sequence}";
    }

    private static string ControlTypeName(int? controlTypeId, string? localizedType)
    {
        if (controlTypeId is { } id && ControlTypes.TryGetValue(id, out var name)) return name;
        if (!string.IsNullOrWhiteSpace(localizedType)) return localizedType.Trim();
        return controlTypeId is { } fallback ? $"ControlType:{fallback}" : "Custom";
    }

    private static readonly IReadOnlyDictionary<int, string> ControlTypes = new Dictionary<int, string>
    {
        [50000] = "Button", [50001] = "Calendar", [50002] = "CheckBox", [50003] = "ComboBox",
        [50004] = "Edit", [50005] = "Hyperlink", [50006] = "Image", [50007] = "ListItem",
        [50008] = "List", [50009] = "Menu", [50010] = "MenuBar", [50011] = "MenuItem",
        [50012] = "ProgressBar", [50013] = "RadioButton", [50014] = "ScrollBar", [50015] = "Slider",
        [50016] = "Spinner", [50017] = "StatusBar", [50018] = "Tab", [50019] = "TabItem",
        [50020] = "Text", [50021] = "ToolBar", [50022] = "ToolTip", [50023] = "Tree",
        [50024] = "TreeItem", [50025] = "Custom", [50026] = "Group", [50027] = "Thumb",
        [50028] = "DataGrid", [50029] = "DataItem", [50030] = "Document", [50031] = "SplitButton",
        [50032] = "Window", [50033] = "Pane", [50034] = "Header", [50035] = "HeaderItem",
        [50036] = "Table", [50037] = "TitleBar", [50038] = "Separator"
    };

    private static PixelRect GetRect(IUiAutomationElementNative element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        try
        {
            if (value is not Array array || array.Length < 4) return PixelRect.Empty;
            var left = Convert.ToDouble(array.GetValue(0), CultureInfo.InvariantCulture);
            var top = Convert.ToDouble(array.GetValue(1), CultureInfo.InvariantCulture);
            var width = Convert.ToDouble(array.GetValue(2), CultureInfo.InvariantCulture);
            var height = Convert.ToDouble(array.GetValue(3), CultureInfo.InvariantCulture);
            if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                return PixelRect.Empty;
            var x1 = CheckedFloor(left);
            var y1 = CheckedFloor(top);
            var x2 = CheckedCeiling(left + width);
            var y2 = CheckedCeiling(top + height);
            return x2 <= x1 || y2 <= y1 ? PixelRect.Empty : new PixelRect(x1, y1, checked(x2 - x1), checked(y2 - y1));
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return PixelRect.Empty;
        }
        finally
        {
            UiAutomationInterop.Release(value);
        }
    }

    private static int CheckedFloor(double value)
    {
        var rounded = Math.Floor(value);
        if (rounded < int.MinValue || rounded > int.MaxValue) throw new OverflowException("UI Automation coordinate is outside Int32 range.");
        return (int)rounded;
    }

    private static int CheckedCeiling(double value)
    {
        var rounded = Math.Ceiling(value);
        if (rounded < int.MinValue || rounded > int.MaxValue) throw new OverflowException("UI Automation coordinate is outside Int32 range.");
        return (int)rounded;
    }

    private static string? GetString(IUiAutomationElementNative element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        try { return value as string; }
        finally { UiAutomationInterop.Release(value); }
    }

    private static int? GetInt(IUiAutomationElementNative element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        try
        {
            if (value is null || value is bool) return null;
            return value switch
            {
                int i => i,
                short s => s,
                long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
                _ => null
            };
        }
        finally { UiAutomationInterop.Release(value); }
    }

    private static bool? GetBool(IUiAutomationElementNative element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        try { return value is bool boolean ? boolean : null; }
        finally { UiAutomationInterop.Release(value); }
    }

    private static int[]? GetIntArray(IUiAutomationElementNative element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        try
        {
            if (value is int[] ints) return ints.Length <= 64 ? ints : ints[..64];
            if (value is not Array array || array.Length == 0 || array.Length > 64) return null;
            var result = new int[array.Length];
            for (var i = 0; i < array.Length; i++) result[i] = Convert.ToInt32(array.GetValue(i), CultureInfo.InvariantCulture);
            return result;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
        finally { UiAutomationInterop.Release(value); }
    }

    private static object? GetProperty(IUiAutomationElementNative element, int propertyId)
    {
        var hr = element.GetCachedPropertyValue(propertyId, out var value);
        return hr < 0 ? null : value;
    }

    private static string? GetProcessName(int processId, Dictionary<int, string?> cache)
    {
        if (processId <= 0) return null;
        if (cache.TryGetValue(processId, out var existing)) return existing;
        try
        {
            using var process = Process.GetProcessById(processId);
            existing = string.IsNullOrWhiteSpace(process.ProcessName) ? null : process.ProcessName;
        }
        catch (ArgumentException) { existing = null; }
        catch (InvalidOperationException) { existing = null; }
        catch (Win32Exception) { existing = null; }
        cache[processId] = existing;
        return existing;
    }

    private static Task<T> RunMtaAsync<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(operation()); }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { completion.TrySetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Magic Capture UI Automation snapshot"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return completion.Task;
    }

    private static bool IsRecoverableProviderFailure(Exception ex) =>
        ex is COMException or InvalidComObjectException or ArgumentException or InvalidOperationException or OverflowException;

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }
}
