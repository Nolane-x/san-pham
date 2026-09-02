using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.App.Capture;

public sealed record CaptureAsset(
    Guid Id,
    DateTimeOffset CreatedUtc,
    PixelRect PixelBounds,
    byte[] PngBytes,
    int Width,
    int Height,
    CaptureSourceKind SourceKind,
    string? SourceDisplayName,
    string? WindowTitle = null,
    string? ProcessName = null,
    string? MonitorName = null,
    IReadOnlyList<ScreenUiAutomationNode>? UiAutomationNodes = null,
    string? ExecutablePath = null)
{
    public static CaptureAsset Create(PixelRect bounds, byte[] pngBytes, CaptureSourceKind kind, string? sourceName = null, string? windowTitle = null, string? processName = null, string? monitorName = null, IReadOnlyList<ScreenUiAutomationNode>? uiAutomationNodes = null, string? executablePath = null) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, bounds, pngBytes, bounds.Width, bounds.Height, kind, sourceName, windowTitle, processName, monitorName, uiAutomationNodes, executablePath);

    public CaptureAsset WithPng(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (!PngDimensions.TryRead(pngBytes, out var width, out var height))
            throw new InvalidDataException("Workflow image payload is not a valid PNG.");
        return this with
        {
            PngBytes = pngBytes,
            Width = width,
            Height = height,
            UiAutomationNodes = width == Width && height == Height ? UiAutomationNodes : null
        };
    }
}
