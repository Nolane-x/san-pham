namespace Magic.Capture.Core.Capture;

public sealed record CaptureSizePreset(string Id, string Name, int Width, int Height)
{
    public override string ToString() => Name;
}

public static class CaptureSizePresets
{
    public static IReadOnlyList<CaptureSizePreset> BuiltIn { get; } =
    [
        new("720p", "HD 720p · 1280×720", 1280, 720),
        new("1080p", "Full HD · 1920×1080", 1920, 1080),
        new("1440p", "QHD · 2560×1440", 2560, 1440),
        new("4k", "4K UHD · 3840×2160", 3840, 2160),
        new("square-1080", "Square · 1080×1080", 1080, 1080),
        new("social-portrait", "Social portrait · 1080×1350", 1080, 1350),
        new("story-portrait", "Story / vertical · 1080×1920", 1080, 1920)
    ];
}
