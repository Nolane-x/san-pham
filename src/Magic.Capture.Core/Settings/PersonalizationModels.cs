using Magic.Capture.Core.Annotation;

namespace Magic.Capture.Core.Settings;

public enum PersonalHotkeyKind
{
    Capture,
    Workflow,
    MagicAction,
    Editor
}

public enum CaptureHotkeyAction
{
    Region,
    ForegroundWindow,
    ActiveMonitor,
    VirtualDesktop,
    RepeatRegion
}

public enum SettingsSection
{
    Hotkeys,
    Capture,
    Output,
    Privacy,
    History,
    Personalization,
    ContextPreferences
}

public sealed record PersonalHotkeyBinding(
    string Id,
    string Name,
    PersonalHotkeyKind Kind,
    string Target,
    HotkeyGesture Gesture,
    bool Enabled = true);

public sealed record PersonalizationActionItem(string Id, bool Visible = true)
{
    public string Display => $"{Id} · {(Visible ? "Shown" : "Hidden")}";
}

public sealed record AnnotationStylePreset(
    string Id,
    string Name,
    uint Argb = 0xFFFF3B30,
    float StrokeWidth = 3f,
    float Opacity = 1f,
    uint? FillArgb = null,
    string FontFamily = "Segoe UI",
    float FontSize = 18f,
    bool FontBold = false,
    bool FontItalic = false,
    AnnotationTextAlignment TextAlignment = AnnotationTextAlignment.Left);

public sealed record MonitorCapturePreference(
    string DeviceName,
    bool? CaptureCursor = null,
    PostCaptureAction? PostCaptureAction = null);

public sealed record AppCaptureRule(
    string Id,
    string ExecutableName,
    string CaptureProfileId,
    bool Enabled = true,
    bool? CaptureCursor = null,
    PostCaptureAction? PostCaptureAction = null);
