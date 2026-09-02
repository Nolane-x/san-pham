using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Documentation;

public enum DocumentationMouseButton
{
    Left,
    Right,
    Middle
}

public sealed record DocumentationTargetEvidence(
    string StableKey,
    string ControlType,
    string? Name,
    string? AutomationId,
    string? ProcessName,
    string? WindowTitle,
    int ProcessId,
    PixelRect DesktopBounds,
    bool IsPassword);

public sealed record DocumentationClickEvent(
    PixelPoint DesktopPoint,
    DocumentationMouseButton Button,
    DateTimeOffset TimestampUtc);

public sealed record DocumentationCapturePlan(
    PixelRect Bounds,
    PixelPoint LocalClick,
    DocumentationTargetEvidence? Target);

public sealed record DocumentationStep(
    string Id,
    DateTimeOffset CapturedUtc,
    string ImageKey,
    int Width,
    int Height,
    DocumentationTargetEvidence? Target,
    PixelPoint? ClickPoint,
    DocumentationMouseButton? MouseButton,
    string? SafeKeyGesture,
    string? Title,
    string? Description,
    string? Section);

public sealed record DocumentationProject(
    int SchemaVersion,
    Guid ProjectId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    string Title,
    string? Subtitle,
    IReadOnlyList<DocumentationStep> Steps,
    string? Header = null,
    string? Footer = null,
    string? LogoImageKey = null,
    string? Template = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string ProductName = "Magic Capture Desktop";

    public static DocumentationProject Create(string title, IReadOnlyList<DocumentationStep>? steps = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new DocumentationProject(
            CurrentSchemaVersion,
            Guid.NewGuid(),
            now,
            now,
            title,
            null,
            steps ?? [],
            null,
            null,
            null,
            DocumentationTemplateCatalog.DefaultId);
    }
}
