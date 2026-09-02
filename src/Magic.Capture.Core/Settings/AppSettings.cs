using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Privacy;

namespace Magic.Capture.Core.Settings;

public enum PostCaptureAction { ResultWindow, CopyImage, PinImage, Save }
public enum AppTheme { System, Light, Dark }
public enum CaptureOverlayTheme { Dark, Light }

public sealed record AppSettings
{
    public int PersistenceSchemaVersion { get; init; } = AppSettingsRules.CurrentPersistenceSchemaVersion;
    public HotkeyGesture RegionHotkey { get; init; } = HotkeyGesture.DefaultRegion;
    public HotkeyGesture RepeatHotkey { get; init; } = HotkeyGesture.DefaultRepeat;
    public bool KeepResident { get; init; } = true;
    public bool CaptureCursor { get; init; }
    public PostCaptureAction DefaultPostCaptureAction { get; init; } = PostCaptureAction.ResultWindow;
    public bool AutoCopyImage { get; init; }
    public bool HistoryEnabled { get; init; } = true;
    public int? HistoryMaximumAgeDays { get; init; } = 30;
    public int? HistoryMaximumCount { get; init; } = 500;
    public long? HistoryMaximumBytes { get; init; }
    public string FileNameTemplate { get; init; } = "Magic Capture Desktop_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}";
    public int JpegQuality { get; init; } = 92;
    public string? PreferredOcrLanguage { get; init; }
    public double PinOpacity { get; init; } = 1.0;
    public int? PinLastX { get; init; }
    public int? PinLastY { get; init; }
    public int? PinLastWidth { get; init; }
    public int? PinLastHeight { get; init; }
    public IReadOnlyList<string> ColorHistory { get; init; } = [];
    public IReadOnlyList<string> ColorSwatches { get; init; } = [];
    public AppTheme Theme { get; init; } = AppTheme.System;
    public CaptureOverlayTheme CaptureOverlayTheme { get; init; } = CaptureOverlayTheme.Dark;
    public bool EnableAiResultCache { get; init; } = true;
    public int AiCacheMaximumAgeDays { get; init; } = 14;
    public int AiCacheMaximumEntries { get; init; } = 500;
    public string? DefaultWorkflowId { get; init; }
    public IReadOnlyList<CaptureProfile> CaptureProfiles { get; init; } = [];
    public IReadOnlyList<PixelRect> RecentRegions { get; init; } = [];
    public string? DefaultCaptureProfileId { get; init; }
    public bool RedactBeforeCopy { get; init; }
    public bool RedactBeforeSave { get; init; }
    public bool RedactBeforePin { get; init; }
    public bool RedactBeforeWorkflow { get; init; }
    public RedactionStyle OutboundRedactionStyle { get; init; } = RedactionStyle.Pixelate;
    public IReadOnlyList<SensitivePattern> SensitivePatterns { get; init; } = [];
    public IReadOnlyList<string> SensitiveWords { get; init; } = [];
    public IReadOnlyList<PersonalHotkeyBinding> PersonalHotkeys { get; init; } = [];
    public IReadOnlyList<PersonalizationActionItem> ToolbarActions { get; init; } = AppSettingsRules.DefaultToolbarActions;
    public IReadOnlyList<PersonalizationActionItem> OverlayActions { get; init; } = AppSettingsRules.DefaultOverlayActions;
    public AnnotationKind DefaultAnnotationTool { get; init; } = AnnotationKind.Rectangle;
    public AnnotationKind? LastAnnotationTool { get; init; }
    public bool RememberLastAnnotationTool { get; init; } = true;
    public IReadOnlyList<AnnotationStylePreset> AnnotationStylePresets { get; init; } = [];
    public IReadOnlyList<MonitorCapturePreference> MonitorPreferences { get; init; } = [];
    public IReadOnlyList<AppCaptureRule> AppCaptureRules { get; init; } = [];
}
