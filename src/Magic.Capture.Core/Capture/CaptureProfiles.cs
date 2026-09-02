using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Capture;

public enum CaptureProfileSource
{
    Region,
    ForegroundWindow,
    ActiveMonitor,
    VirtualDesktop,
    Scrolling
}

public sealed record CaptureProfile(
    string Id,
    string Name,
    CaptureProfileSource Source,
    PixelRect? Region = null,
    bool CaptureCursor = false,
    int DelayMilliseconds = 0,
    PostCaptureAction PostCaptureAction = PostCaptureAction.ResultWindow,
    string? WorkflowId = null,
    string FileFormat = "png")
{
    public CaptureProfile Normalize()
    {
        var id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        var name = string.IsNullOrWhiteSpace(Name) ? "Capture profile" : Name.Trim();
        var delay = Math.Clamp(DelayMilliseconds, 0, 60_000);
        var format = string.IsNullOrWhiteSpace(FileFormat) ? "png" : FileFormat.Trim().TrimStart('.').ToLowerInvariant();
        return this with { Id = id, Name = name, DelayMilliseconds = delay, FileFormat = format };
    }
}

public static class CaptureRegionRules
{
    public static PixelRect Normalize(PixelRect requested, PixelRect desktopBounds)
    {
        if (requested.IsEmpty || desktopBounds.IsEmpty) return PixelRect.Empty;
        return requested.Intersect(desktopBounds);
    }

    public static PixelRect FromExactSize(PixelPoint origin, int width, int height, PixelRect desktopBounds)
    {
        if (width <= 0 || height <= 0) return PixelRect.Empty;
        return Normalize(new PixelRect(origin.X, origin.Y, width, height), desktopBounds);
    }
}

public static class RecentCaptureRegions
{
    public const int DefaultMaximum = 12;
    public const int HardMaximum = 50;

    public static IReadOnlyList<PixelRect> Push(IReadOnlyList<PixelRect>? current, PixelRect region, int maximum = DefaultMaximum)
    {
        if (region.IsEmpty) return current?.ToArray() ?? [];
        maximum = Math.Clamp(maximum, 1, HardMaximum);
        var result = new List<PixelRect>(Math.Min(maximum, (current?.Count ?? 0) + 1)) { region };
        if (current is not null)
        {
            foreach (var candidate in current)
            {
                if (candidate.IsEmpty || candidate == region || result.Contains(candidate)) continue;
                result.Add(candidate);
                if (result.Count == maximum) break;
            }
        }
        return result;
    }
}
