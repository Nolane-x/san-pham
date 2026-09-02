using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.App.Platform;

internal sealed class PersonalHotkeyEventArgs(PersonalHotkeyBinding binding) : EventArgs
{
    public PersonalHotkeyBinding Binding { get; } = binding;
}

internal sealed class HotkeyService : IDisposable
{
    private const int RegionHotkeyId = 0x4D43;
    private const int RepeatHotkeyId = 0x4D44;
    internal const int FirstPersonalHotkeyId = 0x5400;
    private readonly IntPtr _windowHandle;
    private readonly NativeMessageRouter _router;
    private readonly Dictionary<int, PersonalHotkeyBinding> _personalByNativeId = [];
    private bool _regionRegistered;
    private bool _repeatRegistered;
    private HotkeyGesture? _activeRegionGesture;
    private HotkeyGesture? _activeRepeatGesture;

    public HotkeyService(IntPtr windowHandle, NativeMessageRouter router)
    {
        _windowHandle = windowHandle;
        _router = router;
        _router.MessageReceived += OnMessage;
    }

    public event EventHandler? RegionCaptureRequested;
    public event EventHandler? RepeatRegionRequested;
    public event EventHandler<PersonalHotkeyEventArgs>? PersonalHotkeyRequested;
    public string? LastRegistrationError { get; private set; }
    public string? LastRepeatRegistrationError { get; private set; }
    public IReadOnlyDictionary<string, string> PersonalRegistrationErrors { get; private set; } = new Dictionary<string, string>();
    public bool LastRollbackSucceeded { get; private set; } = true;
    public string? LastRollbackError { get; private set; }
    public HotkeyGesture? ActiveRegionHotkey => _activeRegionGesture;
    public HotkeyGesture? ActiveRepeatHotkey => _activeRepeatGesture;
    public IReadOnlyList<PersonalHotkeyBinding> RegisteredPersonalHotkeys => _personalByNativeId.Values.ToArray();

    private bool RegisterRegionCapture(HotkeyGesture gesture)
    {
        UnregisterRegionCapture();
        var modifiers = ToNativeModifiers(gesture.Modifiers) | NativeConstants.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(_windowHandle, RegionHotkeyId, modifiers, (uint)gesture.VirtualKey))
        {
            LastRegistrationError = "The configured region-capture hotkey is already in use or Windows refused registration.";
            return false;
        }
        LastRegistrationError = null;
        _regionRegistered = true;
        _activeRegionGesture = gesture;
        return true;
    }

    private bool RegisterRepeatCapture(HotkeyGesture gesture)
    {
        UnregisterRepeatCapture();
        var modifiers = ToNativeModifiers(gesture.Modifiers) | NativeConstants.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(_windowHandle, RepeatHotkeyId, modifiers, (uint)gesture.VirtualKey))
        {
            LastRepeatRegistrationError = "The repeat-region hotkey is already in use or Windows refused registration.";
            return false;
        }
        LastRepeatRegistrationError = null;
        _repeatRegistered = true;
        _activeRepeatGesture = gesture;
        return true;
    }

    public bool TryApplyConfiguration(
        HotkeyGesture region,
        HotkeyGesture repeat,
        IReadOnlyList<PersonalHotkeyBinding>? personal,
        bool enableRepeat)
    {
        var previousRegion = _activeRegionGesture;
        var previousRepeat = _activeRepeatGesture;
        var previousRepeatWasActive = _repeatRegistered && previousRepeat is not null;
        var previousPersonal = RegisteredPersonalHotkeys.ToArray();

        LastRegistrationError = null;
        LastRepeatRegistrationError = null;
        PersonalRegistrationErrors = new Dictionary<string, string>();
        LastRollbackSucceeded = true;
        LastRollbackError = null;

        UnregisterPersonalHotkeys();
        UnregisterRegionCapture();
        UnregisterRepeatCapture();

        var regionOk = RegisterRegionCapture(region);
        var repeatOk = !enableRepeat || RegisterRepeatCapture(repeat);
        if (!enableRepeat)
        {
            LastRepeatRegistrationError = null;
            UnregisterRepeatCapture();
        }
        var normalized = (personal ?? []).Where(item => item.Enabled).Take(AppSettingsRules.MaximumPersonalHotkeys).ToArray();
        var personalErrors = RegisterPersonalHotkeysCore(normalized);
        PersonalRegistrationErrors = personalErrors;
        var personalOk = personalErrors.Count == 0;
        if (regionOk && repeatOk && personalOk) return true;

        var desiredRegionError = LastRegistrationError;
        var desiredRepeatError = LastRepeatRegistrationError;
        var desiredPersonalErrors = new Dictionary<string, string>(PersonalRegistrationErrors, StringComparer.Ordinal);

        UnregisterPersonalHotkeys();
        UnregisterRegionCapture();
        UnregisterRepeatCapture();

        var rollbackErrors = new List<string>();
        if (previousRegion is { } oldRegion && !RegisterRegionCapture(oldRegion)) rollbackErrors.Add("region hotkey");
        if (enableRepeat && previousRepeatWasActive && previousRepeat is { } oldRepeat && !RegisterRepeatCapture(oldRepeat)) rollbackErrors.Add("repeat hotkey");
        if (!enableRepeat) UnregisterRepeatCapture();
        var restorePersonalErrors = RegisterPersonalHotkeysCore(previousPersonal);
        if (restorePersonalErrors.Count > 0) rollbackErrors.Add("personal hotkeys");

        LastRollbackSucceeded = rollbackErrors.Count == 0;
        LastRollbackError = LastRollbackSucceeded ? null : "Windows also refused rollback of: " + string.Join(", ", rollbackErrors) + ".";
        LastRegistrationError = desiredRegionError;
        LastRepeatRegistrationError = desiredRepeatError;
        PersonalRegistrationErrors = desiredPersonalErrors;
        return false;
    }

    private Dictionary<string, string> RegisterPersonalHotkeysCore(IReadOnlyList<PersonalHotkeyBinding> bindings)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            var nativeId = FirstPersonalHotkeyId + index;
            var modifiers = ToNativeModifiers(binding.Gesture.Modifiers) | NativeConstants.ModNoRepeat;
            if (!NativeMethods.RegisterHotKey(_windowHandle, nativeId, modifiers, (uint)binding.Gesture.VirtualKey))
            {
                errors[binding.Id] = "Hotkey is already in use or Windows refused registration.";
                continue;
            }
            _personalByNativeId[nativeId] = binding;
        }
        return errors;
    }

    private void UnregisterRegionCapture()
    {
        if (_regionRegistered) NativeMethods.UnregisterHotKey(_windowHandle, RegionHotkeyId);
        _regionRegistered = false;
        _activeRegionGesture = null;
    }

    private void UnregisterRepeatCapture()
    {
        if (_repeatRegistered) NativeMethods.UnregisterHotKey(_windowHandle, RepeatHotkeyId);
        _repeatRegistered = false;
        _activeRepeatGesture = null;
    }

    private void UnregisterPersonalHotkeys()
    {
        foreach (var id in _personalByNativeId.Keys.ToArray()) NativeMethods.UnregisterHotKey(_windowHandle, id);
        _personalByNativeId.Clear();
    }

    private void OnMessage(object? sender, NativeWindowMessage message)
    {
        if (message.Message != NativeConstants.WmHotkey) return;
        var id = unchecked((int)message.WParam);
        if (id == RegionHotkeyId) RegionCaptureRequested?.Invoke(this, EventArgs.Empty);
        else if (id == RepeatHotkeyId) RepeatRegionRequested?.Invoke(this, EventArgs.Empty);
        else if (_personalByNativeId.TryGetValue(id, out var binding)) PersonalHotkeyRequested?.Invoke(this, new PersonalHotkeyEventArgs(binding));
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
        UnregisterPersonalHotkeys();
        UnregisterRegionCapture();
        UnregisterRepeatCapture();
    }
}
