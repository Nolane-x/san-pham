using System.Diagnostics;
using System.Text;
using Magic.Capture.App.Commerce;
using Magic.Capture.App.Persistence;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed class ResidentWorkflowTriggerEngine : IAsyncDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly NativeMessageRouter _router;
    private readonly WorkflowTriggerStore _store;
    private readonly WorkflowTriggerRunner _runner;
    private readonly WorkflowTriggerHotkeyService _hotkeys;
    private readonly EntitlementService _entitlements;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private readonly Dictionary<string, DateTimeOffset> _lastFileSignal = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingTriggerIds = new(StringComparer.Ordinal);
    private readonly NativeMethods.WinEventProc _foregroundCallback;
    private IReadOnlyList<WorkflowTrigger> _clipboardTriggers = [];
    private IReadOnlyList<WorkflowTrigger> _windowTriggers = [];
    private IReadOnlyList<WorkflowTrigger> _processTriggers = [];
    private IntPtr _foregroundHook;
    private bool _clipboardListenerRegistered;
    private CancellationTokenSource? _processLoopCts;
    private Task? _processLoopTask;
    private bool _disposed;

    public ResidentWorkflowTriggerEngine(
        IntPtr windowHandle,
        NativeMessageRouter router,
        WorkflowTriggerStore store,
        WorkflowTriggerRunner runner,
        WorkflowTriggerHotkeyService hotkeys,
        EntitlementService entitlements,
        LocalLog log)
    {
        _windowHandle = windowHandle;
        _router = router;
        _store = store;
        _runner = runner;
        _hotkeys = hotkeys;
        _entitlements = entitlements;
        _log = log;
        _foregroundCallback = OnForegroundChanged;
        _router.MessageReceived += OnWindowMessage;
        _hotkeys.Triggered += OnHotkeyTriggered;
        _store.Changed += OnConfigurationChanged;
        _entitlements.Changed += OnEntitlementChanged;
    }

    public IReadOnlyDictionary<string, string> HotkeyRegistrationErrors => _hotkeys.RegistrationErrors;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            await StopSourcesAsync();
            if (!_entitlements.CanUse(ProductFeature.AdvancedWorkflows)) return;

            var triggers = (await _store.LoadAsync(cancellationToken)).Where(trigger => trigger.Enabled).ToArray();
            ConfigureFileWatchers(triggers.Where(trigger => trigger.Kind == WorkflowTriggerKind.FileChange).ToArray());
            ConfigureClipboard(triggers.Where(trigger => trigger.Kind == WorkflowTriggerKind.ClipboardChange).ToArray());
            ConfigureForegroundWindow(triggers.Where(trigger => trigger.Kind == WorkflowTriggerKind.ForegroundWindow).ToArray());
            ConfigureProcessLoop(triggers.Where(trigger => trigger.Kind == WorkflowTriggerKind.ProcessStart).ToArray());
            _hotkeys.Register(triggers.Where(trigger => trigger.Kind == WorkflowTriggerKind.Hotkey).ToArray());
        }
        finally { _reloadGate.Release(); }
    }

    public async Task StopAsync()
    {
        await _reloadGate.WaitAsync();
        try { await StopSourcesAsync(); }
        finally { _reloadGate.Release(); }
    }

    private void ConfigureFileWatchers(IReadOnlyList<WorkflowTrigger> triggers)
    {
        foreach (var trigger in triggers)
        {
            var options = trigger.FileChange;
            if (options is null || !Directory.Exists(options.FolderPath)) continue;
            try
            {
                var watcher = new FileSystemWatcher(options.FolderPath, string.IsNullOrWhiteSpace(options.Filter) ? "*.*" : options.Filter)
                {
                    IncludeSubdirectories = options.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                    InternalBufferSize = 16 * 1024,
                    EnableRaisingEvents = false
                };
                FileSystemEventHandler changed = (_, _) => OnFileSignal(trigger.Id);
                RenamedEventHandler renamed = (_, _) => OnFileSignal(trigger.Id);
                ErrorEventHandler error = (_, _) => ScheduleReloadAfterWatcherError();
                watcher.Created += changed;
                watcher.Changed += changed;
                watcher.Renamed += renamed;
                watcher.Error += error;
                watcher.EnableRaisingEvents = true;
                _fileWatchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log.Error("WorkflowTriggerFileWatcher", ex);
            }
        }
    }

    private void ConfigureClipboard(IReadOnlyList<WorkflowTrigger> triggers)
    {
        _clipboardTriggers = triggers;
        if (triggers.Count == 0) return;
        if (!NativeMethods.AddClipboardFormatListener(_windowHandle))
        {
            _log.Error("WorkflowTriggerClipboard", new InvalidOperationException("Windows refused clipboard listener registration."));
            _clipboardTriggers = [];
            return;
        }
        _clipboardListenerRegistered = true;
    }

    private void ConfigureForegroundWindow(IReadOnlyList<WorkflowTrigger> triggers)
    {
        _windowTriggers = triggers;
        if (triggers.Count == 0) return;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeConstants.EventSystemForeground,
            NativeConstants.EventSystemForeground,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            NativeConstants.WinEventOutOfContext | NativeConstants.WinEventSkipOwnProcess);
        if (_foregroundHook == IntPtr.Zero)
        {
            _log.Error("WorkflowTriggerForeground", new InvalidOperationException("Windows refused foreground window hook registration."));
            _windowTriggers = [];
        }
    }

    private void ConfigureProcessLoop(IReadOnlyList<WorkflowTrigger> triggers)
    {
        _processTriggers = triggers;
        if (triggers.Count == 0) return;
        _processLoopCts = new CancellationTokenSource();
        _processLoopTask = ProcessLoopAsync(_processLoopCts.Token);
    }

    private void OnFileSignal(string triggerId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lastFileSignal)
        {
            if (_lastFileSignal.TryGetValue(triggerId, out var last) && now - last < TimeSpan.FromMilliseconds(750)) return;
            _lastFileSignal[triggerId] = now;
        }
        Fire(triggerId, WorkflowTriggerKind.FileChange, "file_change");
    }

    private void OnWindowMessage(object? sender, NativeWindowMessage message)
    {
        if (message.Message != NativeConstants.WmClipboardUpdate || _clipboardTriggers.Count == 0) return;
        foreach (var trigger in _clipboardTriggers) Fire(trigger.Id, WorkflowTriggerKind.ClipboardChange, "clipboard_change");
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hWnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hWnd == IntPtr.Zero || _windowTriggers.Count == 0) return;
        var text = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(hWnd, text, text.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        var processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            processName = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        var haystack = processName + "\n" + text;
        foreach (var trigger in _windowTriggers)
        {
            var pattern = trigger.Window?.Pattern;
            if (!string.IsNullOrWhiteSpace(pattern) && haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                Fire(trigger.Id, WorkflowTriggerKind.ForegroundWindow, "foreground_window");
        }
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        var seen = SnapshotProcessIds();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var current = new HashSet<int>();
                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            current.Add(process.Id);
                            if (seen.Contains(process.Id)) continue;
                            var processName = process.ProcessName;
                            foreach (var trigger in _processTriggers)
                            {
                                var configured = trigger.Process?.ProcessName;
                                if (string.IsNullOrWhiteSpace(configured)) continue;
                                var normalized = configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? configured[..^4] : configured;
                                if (string.Equals(processName, normalized, StringComparison.OrdinalIgnoreCase))
                                    Fire(trigger.Id, WorkflowTriggerKind.ProcessStart, "process_start");
                            }
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    }
                }
                seen = current;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { _log.Error("WorkflowTriggerProcessLoop", ex); }
    }

    private static HashSet<int> SnapshotProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { result.Add(process.Id); }
                catch (InvalidOperationException) { }
            }
        }
        return result;
    }

    private void OnHotkeyTriggered(object? sender, WorkflowTriggerHotkeyEventArgs e) => Fire(e.TriggerId, WorkflowTriggerKind.Hotkey, "hotkey");
    private void OnConfigurationChanged(object? sender, EventArgs e) => ScheduleReload();
    private void OnEntitlementChanged(object? sender, Magic.Capture.Core.Commerce.EntitlementSnapshot e) => ScheduleReload();

    private void Fire(string triggerId, WorkflowTriggerKind expectedKind, string reasonCode)
    {
        lock (_pendingTriggerIds)
        {
            if (!_pendingTriggerIds.Add(triggerId)) return;
        }
        _ = FireAsync(triggerId, expectedKind, reasonCode);
    }

    private async Task FireAsync(string triggerId, WorkflowTriggerKind expectedKind, string reasonCode)
    {
        try { await _runner.RunAsync(triggerId, expectedKind, reasonCode); }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { _log.Error("WorkflowTriggerEvent", ex); }
        finally
        {
            lock (_pendingTriggerIds) _pendingTriggerIds.Remove(triggerId);
        }
    }

    private void ScheduleReload() => _ = ReloadBestEffortAsync(TimeSpan.Zero);
    private void ScheduleReloadAfterWatcherError() => _ = ReloadBestEffortAsync(TimeSpan.FromSeconds(1));

    private async Task ReloadBestEffortAsync(TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            await ReloadAsync();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex)) { _log.Error("WorkflowTriggerReload", ex); }
    }

    private async Task StopSourcesAsync()
    {
        foreach (var watcher in _fileWatchers)
        {
            try { watcher.EnableRaisingEvents = false; } catch (InvalidOperationException) { }
            watcher.Dispose();
        }
        _fileWatchers.Clear();
        lock (_lastFileSignal) _lastFileSignal.Clear();

        if (_clipboardListenerRegistered)
        {
            _ = NativeMethods.RemoveClipboardFormatListener(_windowHandle);
            _clipboardListenerRegistered = false;
        }
        _clipboardTriggers = [];

        if (_foregroundHook != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        _windowTriggers = [];

        if (_processLoopCts is not null)
        {
            _processLoopCts.Cancel();
            if (_processLoopTask is not null)
            {
                try { await _processLoopTask; } catch (OperationCanceledException) { }
            }
            _processLoopCts.Dispose();
            _processLoopCts = null;
            _processLoopTask = null;
        }
        _processTriggers = [];
        _hotkeys.UnregisterAll();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Changed -= OnConfigurationChanged;
        _entitlements.Changed -= OnEntitlementChanged;
        _router.MessageReceived -= OnWindowMessage;
        _hotkeys.Triggered -= OnHotkeyTriggered;
        await StopAsync();
        _hotkeys.Dispose();
        _reloadGate.Dispose();
    }
}
