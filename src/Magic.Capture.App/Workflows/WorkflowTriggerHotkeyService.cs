using Magic.Capture.App.Platform;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed class WorkflowTriggerHotkeyEventArgs(string triggerId) : EventArgs
{
    public string TriggerId { get; } = triggerId;
}

internal sealed class WorkflowTriggerHotkeyService : IDisposable
{
    private const int FirstHotkeyId = 0x5200;
    private readonly IntPtr _windowHandle;
    private readonly NativeMessageRouter _router;
    private readonly Dictionary<int, string> _triggerByNativeId = [];

    public WorkflowTriggerHotkeyService(IntPtr windowHandle, NativeMessageRouter router)
    {
        _windowHandle = windowHandle;
        _router = router;
        _router.MessageReceived += OnMessage;
    }

    public event EventHandler<WorkflowTriggerHotkeyEventArgs>? Triggered;
    public IReadOnlyDictionary<string, string> RegistrationErrors { get; private set; } = new Dictionary<string, string>();

    public void Register(IReadOnlyList<WorkflowTrigger> triggers)
    {
        UnregisterAll();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var eligible = triggers.Where(trigger => trigger.Enabled && trigger.Kind == WorkflowTriggerKind.Hotkey && trigger.Hotkey is not null)
            .Take(WorkflowTriggerPolicy.MaximumHotkeyTriggers).ToArray();
        for (var index = 0; index < eligible.Length; index++)
        {
            var trigger = eligible[index];
            var gesture = trigger.Hotkey!;
            var nativeId = FirstHotkeyId + index;
            var modifiers = ToNativeModifiers(gesture.Modifiers) | NativeConstants.ModNoRepeat;
            if (!NativeMethods.RegisterHotKey(_windowHandle, nativeId, modifiers, (uint)gesture.VirtualKey))
            {
                errors[trigger.Id] = "Hotkey is already in use or Windows refused registration.";
                continue;
            }
            _triggerByNativeId[nativeId] = trigger.Id;
        }
        RegistrationErrors = errors;
    }

    public void UnregisterAll()
    {
        foreach (var id in _triggerByNativeId.Keys.ToArray()) NativeMethods.UnregisterHotKey(_windowHandle, id);
        _triggerByNativeId.Clear();
        RegistrationErrors = new Dictionary<string, string>();
    }

    private void OnMessage(object? sender, NativeWindowMessage message)
    {
        if (message.Message != NativeConstants.WmHotkey) return;
        var id = unchecked((int)message.WParam.ToUInt64());
        if (_triggerByNativeId.TryGetValue(id, out var triggerId)) Triggered?.Invoke(this, new WorkflowTriggerHotkeyEventArgs(triggerId));
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) value |= NativeConstants.ModAlt;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) value |= NativeConstants.ModControl;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) value |= NativeConstants.ModShift;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) value |= NativeConstants.ModWin;
        return value;
    }

    public void Dispose()
    {
        _router.MessageReceived -= OnMessage;
        UnregisterAll();
    }
}
