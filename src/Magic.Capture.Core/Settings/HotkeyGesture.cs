namespace Magic.Capture.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public static HotkeyGesture DefaultRegion => new(HotkeyModifiers.Windows | HotkeyModifiers.Shift, 0x58); // X
    public static HotkeyGesture DefaultRepeat => new(HotkeyModifiers.Windows | HotkeyModifiers.Shift, 0x52); // R
}
