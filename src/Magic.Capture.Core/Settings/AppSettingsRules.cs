using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Privacy;

namespace Magic.Capture.Core.Settings;

public static class AppSettingsRules
{
    public const int CurrentPersistenceSchemaVersion = 2;
    public const int MaximumFileNameTemplateLength = 240;
    public const int MaximumOcrLanguageLength = 64;
    public const int MaximumCaptureProfiles = 64;
    public const int MaximumRecentRegions = 16;
    public const int MaximumProfileNameLength = 120;
    public const int MaximumIdentifierLength = 128;
    public const int MaximumSensitivePatterns = 24;
    public const int MaximumSensitiveWords = 64;
    public const int MaximumSensitivePatternLabelLength = 64;
    public const int MaximumSensitivePatternLength = 512;
    public const int MaximumSensitiveWordLength = 128;
    public const int MaximumColorHistory = 32;
    public const int MaximumColorSwatches = 24;
    public const int MaximumPersonalHotkeys = 48;
    public const int MaximumAnnotationStylePresets = 24;
    public const int MaximumMonitorPreferences = 32;
    public const int MaximumAppCaptureRules = 64;
    public const int MaximumPersonalizationNameLength = 160;

    public static IReadOnlyList<PersonalizationActionItem> DefaultToolbarActions { get; } =
    [
        new("undo"), new("redo"), new("rotate"), new("resize"), new("copy"), new("save"), new("pin")
    ];

    public static IReadOnlyList<PersonalizationActionItem> DefaultOverlayActions { get; } =
    [
        new("copy"), new("save"), new("pin"), new("text"), new("table"), new("barcode"), new("edit"), new("color"), new("magic")
    ];

    private static readonly HashSet<string> ToolbarActionIds = new(DefaultToolbarActions.Select(item => item.Id), StringComparer.Ordinal);
    private static readonly HashSet<string> OverlayActionIds = new(DefaultOverlayActions.Select(item => item.Id), StringComparer.Ordinal);

    private const int MaximumHistoryAgeDays = 36_500;
    private const int MaximumHistoryCount = 100_000;
    private const long MaximumHistoryBytes = 16L * 1024 * 1024 * 1024 * 1024;
    private const int MaximumAiCacheAgeDays = 3_650;
    private const int MaximumAiCacheEntries = 5_000;

    public static AppSettings NormalizeForRuntime(AppSettings? settings)
    {
        settings ??= new AppSettings();
        var defaults = new AppSettings();

        var profiles = NormalizeProfiles(settings.CaptureProfiles);
        var recentRegions = NormalizeRecentRegions(settings.RecentRegions);
        var defaultProfileId = NormalizeOptionalText(settings.DefaultCaptureProfileId, MaximumIdentifierLength);
        var sensitivePatterns = NormalizeSensitivePatterns(settings.SensitivePatterns);
        var sensitiveWords = NormalizeSensitiveWords(settings.SensitiveWords);
        var personalHotkeys = NormalizePersonalHotkeys(settings.PersonalHotkeys, settings.RegionHotkey, settings.RepeatHotkey);
        var toolbarActions = NormalizeActionLayout(settings.ToolbarActions, DefaultToolbarActions, ToolbarActionIds);
        var overlayActions = NormalizeActionLayout(settings.OverlayActions, DefaultOverlayActions, OverlayActionIds);
        var annotationStyles = NormalizeAnnotationStyles(settings.AnnotationStylePresets);
        var monitorPreferences = NormalizeMonitorPreferences(settings.MonitorPreferences);
        var appCaptureRules = NormalizeAppCaptureRules(settings.AppCaptureRules, profiles);
        if (defaultProfileId is not null && !profiles.Any(profile => string.Equals(profile.Id, defaultProfileId, StringComparison.Ordinal)))
            defaultProfileId = null;

        return settings with
        {
            PersistenceSchemaVersion = CurrentPersistenceSchemaVersion,
            RegionHotkey = NormalizeHotkey(settings.RegionHotkey, HotkeyGesture.DefaultRegion),
            RepeatHotkey = NormalizeHotkey(settings.RepeatHotkey, HotkeyGesture.DefaultRepeat),
            DefaultPostCaptureAction = Enum.IsDefined(typeof(PostCaptureAction), settings.DefaultPostCaptureAction)
                ? settings.DefaultPostCaptureAction
                : PostCaptureAction.ResultWindow,
            HistoryMaximumAgeDays = NormalizeNullableInt(settings.HistoryMaximumAgeDays, 0, MaximumHistoryAgeDays),
            HistoryMaximumCount = NormalizeNullableInt(settings.HistoryMaximumCount, 0, MaximumHistoryCount),
            HistoryMaximumBytes = NormalizeNullableLong(settings.HistoryMaximumBytes, 0, MaximumHistoryBytes),
            FileNameTemplate = NormalizeRequiredText(settings.FileNameTemplate, defaults.FileNameTemplate, MaximumFileNameTemplateLength),
            JpegQuality = Math.Clamp(settings.JpegQuality, 1, 100),
            PreferredOcrLanguage = NormalizeOptionalText(settings.PreferredOcrLanguage, MaximumOcrLanguageLength),
            PinOpacity = double.IsFinite(settings.PinOpacity) ? Math.Clamp(settings.PinOpacity, 0.5, 1.0) : defaults.PinOpacity,
            PinLastX = NormalizeNullableInt(settings.PinLastX, -100_000, 100_000),
            PinLastY = NormalizeNullableInt(settings.PinLastY, -100_000, 100_000),
            PinLastWidth = NormalizeNullableInt(settings.PinLastWidth, 160, 16_384),
            PinLastHeight = NormalizeNullableInt(settings.PinLastHeight, 100, 16_384),
            ColorHistory = NormalizeColors(settings.ColorHistory, MaximumColorHistory),
            ColorSwatches = NormalizeColors(settings.ColorSwatches, MaximumColorSwatches),
            Theme = Enum.IsDefined(typeof(AppTheme), settings.Theme) ? settings.Theme : AppTheme.System,
            CaptureOverlayTheme = Enum.IsDefined(typeof(CaptureOverlayTheme), settings.CaptureOverlayTheme) ? settings.CaptureOverlayTheme : CaptureOverlayTheme.Dark,
            AiCacheMaximumAgeDays = Math.Clamp(settings.AiCacheMaximumAgeDays, 1, MaximumAiCacheAgeDays),
            AiCacheMaximumEntries = Math.Clamp(settings.AiCacheMaximumEntries, 10, MaximumAiCacheEntries),
            DefaultWorkflowId = NormalizeOptionalText(settings.DefaultWorkflowId, MaximumIdentifierLength),
            CaptureProfiles = profiles,
            RecentRegions = recentRegions,
            DefaultCaptureProfileId = defaultProfileId,
            PersonalHotkeys = personalHotkeys,
            ToolbarActions = toolbarActions,
            OverlayActions = overlayActions,
            DefaultAnnotationTool = Enum.IsDefined(typeof(AnnotationKind), settings.DefaultAnnotationTool) ? settings.DefaultAnnotationTool : AnnotationKind.Rectangle,
            LastAnnotationTool = settings.LastAnnotationTool is { } last && Enum.IsDefined(typeof(AnnotationKind), last) ? last : null,
            AnnotationStylePresets = annotationStyles,
            MonitorPreferences = monitorPreferences,
            AppCaptureRules = appCaptureRules,
            OutboundRedactionStyle = Enum.IsDefined(typeof(RedactionStyle), settings.OutboundRedactionStyle) ? settings.OutboundRedactionStyle : RedactionStyle.Pixelate,
            SensitivePatterns = sensitivePatterns,
            SensitiveWords = sensitiveWords
        };
    }

    public static bool IsPersistenceSchemaSupported(int version) =>
        version >= 0 && version <= CurrentPersistenceSchemaVersion;

    public static AppSettings ResetSection(AppSettings settings, SettingsSection section)
    {
        settings = NormalizeForRuntime(settings);
        var defaults = new AppSettings();
        var reset = section switch
        {
            SettingsSection.Hotkeys => settings with
            {
                RegionHotkey = defaults.RegionHotkey,
                RepeatHotkey = defaults.RepeatHotkey,
                PersonalHotkeys = []
            },
            SettingsSection.Capture => settings with
            {
                CaptureCursor = defaults.CaptureCursor,
                DefaultPostCaptureAction = defaults.DefaultPostCaptureAction,
                CaptureOverlayTheme = defaults.CaptureOverlayTheme,
                DefaultCaptureProfileId = null,
                RecentRegions = []
            },
            SettingsSection.Output => settings with
            {
                FileNameTemplate = defaults.FileNameTemplate,
                JpegQuality = defaults.JpegQuality,
                PreferredOcrLanguage = null,
                AutoCopyImage = defaults.AutoCopyImage
            },
            SettingsSection.Privacy => settings with
            {
                RedactBeforeCopy = false,
                RedactBeforeSave = false,
                RedactBeforePin = false,
                RedactBeforeWorkflow = false,
                OutboundRedactionStyle = defaults.OutboundRedactionStyle,
                SensitivePatterns = [],
                SensitiveWords = []
            },
            SettingsSection.History => settings with
            {
                HistoryEnabled = defaults.HistoryEnabled,
                HistoryMaximumAgeDays = defaults.HistoryMaximumAgeDays,
                HistoryMaximumCount = defaults.HistoryMaximumCount,
                HistoryMaximumBytes = defaults.HistoryMaximumBytes
            },
            SettingsSection.Personalization => settings with
            {
                ToolbarActions = DefaultToolbarActions,
                OverlayActions = DefaultOverlayActions,
                DefaultAnnotationTool = defaults.DefaultAnnotationTool,
                LastAnnotationTool = null,
                RememberLastAnnotationTool = defaults.RememberLastAnnotationTool,
                AnnotationStylePresets = []
            },
            SettingsSection.ContextPreferences => settings with
            {
                MonitorPreferences = [],
                AppCaptureRules = []
            },
            _ => settings
        };
        return NormalizeForRuntime(reset);
    }

    private static IReadOnlyList<PersonalHotkeyBinding> NormalizePersonalHotkeys(
        IReadOnlyList<PersonalHotkeyBinding>? bindings,
        HotkeyGesture regionHotkey,
        HotkeyGesture repeatHotkey)
    {
        if (bindings is null || bindings.Count == 0) return [];
        var result = new List<PersonalHotkeyBinding>(Math.Min(bindings.Count, MaximumPersonalHotkeys));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var gestures = new HashSet<string>(StringComparer.Ordinal)
        {
            GestureKey(NormalizeHotkey(regionHotkey, HotkeyGesture.DefaultRegion)),
            GestureKey(NormalizeHotkey(repeatHotkey, HotkeyGesture.DefaultRepeat))
        };
        foreach (var source in bindings)
        {
            if (source is null) continue;
            var id = NormalizeOptionalText(source.Id, MaximumIdentifierLength);
            var name = NormalizeOptionalText(source.Name, MaximumPersonalizationNameLength);
            var target = NormalizeOptionalText(source.Target, MaximumIdentifierLength);
            if (id is null || name is null || target is null || !ids.Add(id)) continue;
            if (!Enum.IsDefined(typeof(PersonalHotkeyKind), source.Kind)) continue;
            if (!IsValidHotkey(source.Gesture)) continue;
            if (!IsValidPersonalHotkeyTarget(source.Kind, target)) continue;
            if (!gestures.Add(GestureKey(source.Gesture))) continue;
            result.Add(source with { Id = id, Name = name, Target = target });
            if (result.Count == MaximumPersonalHotkeys) break;
        }
        return result;
    }

    private static IReadOnlyList<PersonalizationActionItem> NormalizeActionLayout(
        IReadOnlyList<PersonalizationActionItem>? source,
        IReadOnlyList<PersonalizationActionItem> defaults,
        HashSet<string> allowlist)
    {
        if (source is null || source.Count == 0) return defaults.ToArray();
        var result = new List<PersonalizationActionItem>(allowlist.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            if (item is null || !allowlist.Contains(item.Id) || !seen.Add(item.Id)) continue;
            result.Add(new PersonalizationActionItem(item.Id, item.Visible));
        }
        foreach (var item in defaults)
            if (seen.Add(item.Id)) result.Add(item);
        return result;
    }

    private static IReadOnlyList<AnnotationStylePreset> NormalizeAnnotationStyles(IReadOnlyList<AnnotationStylePreset>? styles)
    {
        if (styles is null || styles.Count == 0) return [];
        var result = new List<AnnotationStylePreset>(Math.Min(styles.Count, MaximumAnnotationStylePresets));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in styles)
        {
            if (source is null) continue;
            var id = NormalizeOptionalText(source.Id, MaximumIdentifierLength);
            var name = NormalizeOptionalText(source.Name, MaximumPersonalizationNameLength);
            if (id is null || name is null || !ids.Add(id)) continue;
            var family = NormalizeRequiredText(source.FontFamily, "Segoe UI", 96);
            var align = Enum.IsDefined(typeof(AnnotationTextAlignment), source.TextAlignment) ? source.TextAlignment : AnnotationTextAlignment.Left;
            result.Add(source with
            {
                Id = id,
                Name = name,
                StrokeWidth = float.IsFinite(source.StrokeWidth) ? Math.Clamp(source.StrokeWidth, 1f, 64f) : 3f,
                Opacity = float.IsFinite(source.Opacity) ? Math.Clamp(source.Opacity, 0.05f, 1f) : 1f,
                FontFamily = family,
                FontSize = float.IsFinite(source.FontSize) ? Math.Clamp(source.FontSize, 8f, 256f) : 18f,
                TextAlignment = align
            });
            if (result.Count == MaximumAnnotationStylePresets) break;
        }
        return result;
    }

    private static IReadOnlyList<MonitorCapturePreference> NormalizeMonitorPreferences(IReadOnlyList<MonitorCapturePreference>? preferences)
    {
        if (preferences is null || preferences.Count == 0) return [];
        var result = new List<MonitorCapturePreference>(Math.Min(preferences.Count, MaximumMonitorPreferences));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in preferences)
        {
            if (source is null) continue;
            var device = NormalizeOptionalText(source.DeviceName, MaximumPersonalizationNameLength);
            if (device is null || !names.Add(device)) continue;
            PostCaptureAction? action = source.PostCaptureAction is { } candidate && Enum.IsDefined(typeof(PostCaptureAction), candidate) ? candidate : null;
            result.Add(source with { DeviceName = device, PostCaptureAction = action });
            if (result.Count == MaximumMonitorPreferences) break;
        }
        return result;
    }

    private static IReadOnlyList<AppCaptureRule> NormalizeAppCaptureRules(IReadOnlyList<AppCaptureRule>? rules, IReadOnlyList<CaptureProfile> profiles)
    {
        if (rules is null || rules.Count == 0) return [];
        var profileIds = profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        var result = new List<AppCaptureRule>(Math.Min(rules.Count, MaximumAppCaptureRules));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in rules)
        {
            if (source is null) continue;
            var id = NormalizeOptionalText(source.Id, MaximumIdentifierLength);
            var executable = NormalizeOptionalText(source.ExecutableName, MaximumPersonalizationNameLength);
            var profileId = NormalizeOptionalText(source.CaptureProfileId, MaximumIdentifierLength);
            if (id is null || executable is null || profileId is null || !ids.Add(id)) continue;
            if (!IsSafeExecutableName(executable) || !executables.Add(executable) || !profileIds.Contains(profileId)) continue;
            PostCaptureAction? action = source.PostCaptureAction is { } candidate && Enum.IsDefined(typeof(PostCaptureAction), candidate) ? candidate : null;
            result.Add(source with { Id = id, ExecutableName = executable, CaptureProfileId = profileId, PostCaptureAction = action });
            if (result.Count == MaximumAppCaptureRules) break;
        }
        return result;
    }

    public static bool IsValidHotkey(HotkeyGesture? gesture)
    {
        if (gesture is not { Modifiers: not HotkeyModifiers.None } value) return false;
        const HotkeyModifiers valid = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;
        if ((value.Modifiers & ~valid) != 0) return false;
        return value.VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87;
    }

    private static bool IsValidPersonalHotkeyTarget(PersonalHotkeyKind kind, string target) => kind switch
    {
        PersonalHotkeyKind.Capture => Enum.TryParse<CaptureHotkeyAction>(target, true, out _) ||
            (target.StartsWith("profile:", StringComparison.OrdinalIgnoreCase) &&
             target.Length > "profile:".Length &&
             target["profile:".Length..].All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ':')),
        PersonalHotkeyKind.Editor => string.Equals(target, "open-last", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "open", StringComparison.OrdinalIgnoreCase),
        PersonalHotkeyKind.Workflow or PersonalHotkeyKind.MagicAction => target.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ':'),
        _ => false
    };

    private static string GestureKey(HotkeyGesture gesture) => $"{(int)gesture.Modifiers}:{gesture.VirtualKey}";

    private static bool IsSafeExecutableName(string value)
    {
        if (value.Length is < 1 or > MaximumPersonalizationNameLength) return false;
        if (value.IndexOfAny(['\\', '/', ':']) >= 0) return false;
        if (value is "." or "..") return false;
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && value.All(c => !char.IsControl(c));
    }

    private static IReadOnlyList<CaptureProfile> NormalizeProfiles(IReadOnlyList<CaptureProfile>? profiles)
    {
        if (profiles is null || profiles.Count == 0) return [];

        var result = new List<CaptureProfile>(Math.Min(profiles.Count, MaximumCaptureProfiles));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in profiles)
        {
            if (source is null) continue;

            var profile = source.Normalize();
            var id = NormalizeRequiredText(profile.Id, Guid.NewGuid().ToString("N"), MaximumIdentifierLength);
            if (!ids.Add(id)) continue;

            PixelRect? region = profile.Region is { } candidate && !candidate.IsEmpty ? candidate : null;
            var captureSource = Enum.IsDefined(typeof(CaptureProfileSource), profile.Source)
                ? profile.Source
                : CaptureProfileSource.Region;
            var postCaptureAction = Enum.IsDefined(typeof(PostCaptureAction), profile.PostCaptureAction)
                ? profile.PostCaptureAction
                : PostCaptureAction.ResultWindow;

            result.Add(profile with
            {
                Id = id,
                Name = NormalizeRequiredText(profile.Name, "Capture profile", MaximumProfileNameLength),
                Source = captureSource,
                Region = region,
                DelayMilliseconds = Math.Clamp(profile.DelayMilliseconds, 0, 60_000),
                PostCaptureAction = postCaptureAction,
                WorkflowId = NormalizeOptionalText(profile.WorkflowId, MaximumIdentifierLength),
                FileFormat = NormalizeFileFormat(profile.FileFormat)
            });

            if (result.Count == MaximumCaptureProfiles) break;
        }
        return result;
    }

    private static IReadOnlyList<PixelRect> NormalizeRecentRegions(IReadOnlyList<PixelRect>? regions)
    {
        if (regions is null || regions.Count == 0) return [];
        var result = new List<PixelRect>(Math.Min(regions.Count, MaximumRecentRegions));
        var seen = new HashSet<PixelRect>();
        foreach (var region in regions)
        {
            if (region.IsEmpty || !seen.Add(region)) continue;
            result.Add(region);
            if (result.Count == MaximumRecentRegions) break;
        }
        return result;
    }

    private static IReadOnlyList<SensitivePattern> NormalizeSensitivePatterns(IReadOnlyList<SensitivePattern>? patterns)
    {
        if (patterns is null || patterns.Count == 0) return [];
        var result = new List<SensitivePattern>(Math.Min(patterns.Count, MaximumSensitivePatterns));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in patterns)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Pattern)) continue;
            var label = NormalizeRequiredText(source.Label, "Custom", MaximumSensitivePatternLabelLength);
            var pattern = source.Pattern.Trim();
            if (pattern.Length > MaximumSensitivePatternLength) continue;
            var key = label + "\n" + pattern;
            if (!seen.Add(key)) continue;
            result.Add(new SensitivePattern(label, pattern));
            if (result.Count == MaximumSensitivePatterns) break;
        }
        return result;
    }

    private static IReadOnlyList<string> NormalizeColors(IReadOnlyList<string>? values, int maximum)
    {
        if (values is null || values.Count == 0) return [];
        var result = new List<string>(Math.Min(values.Count, maximum));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var text = value.Trim().ToUpperInvariant();
            if (text.Length == 7 && text[0] == '#' && text[1..].All(Uri.IsHexDigit) && seen.Add(text)) result.Add(text);
            if (result.Count == maximum) break;
        }
        return result;
    }

    private static IReadOnlyList<string> NormalizeSensitiveWords(IReadOnlyList<string>? words)
    {
        if (words is null || words.Count == 0) return [];
        var result = new List<string>(Math.Min(words.Count, MaximumSensitiveWords));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in words)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var word = source.Trim();
            if (word.Length > MaximumSensitiveWordLength) word = word[..MaximumSensitiveWordLength];
            if (word.Length < 2 || !seen.Add(word)) continue;
            result.Add(word);
            if (result.Count == MaximumSensitiveWords) break;
        }
        return result;
    }

    private static HotkeyGesture NormalizeHotkey(HotkeyGesture? gesture, HotkeyGesture fallback)
    {
        if (gesture is null) return fallback;
        const HotkeyModifiers validModifiers = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;
        if ((gesture.Modifiers & ~validModifiers) != 0 || gesture.Modifiers == HotkeyModifiers.None) return fallback;
        if (!((gesture.VirtualKey is >= 0x30 and <= 0x39) || (gesture.VirtualKey is >= 0x41 and <= 0x5A))) return fallback;
        return gesture;
    }

    private static int? NormalizeNullableInt(int? value, int minimum, int maximum) =>
        value is null ? null : Math.Clamp(value.Value, minimum, maximum);

    private static long? NormalizeNullableLong(long? value, long minimum, long maximum) =>
        value is null || value.Value < minimum ? null : Math.Clamp(value.Value, minimum, maximum);

    private static string NormalizeRequiredText(string? value, string fallback, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string NormalizeFileFormat(string? value)
    {
        var format = string.IsNullOrWhiteSpace(value) ? "png" : value.Trim().TrimStart('.').ToLowerInvariant();
        return format is "png" or "jpg" or "jpeg" or "bmp" or "tif" or "tiff" or "pdf" ? format : "png";
    }
}
