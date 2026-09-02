using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Annotation;

public enum AnnotationKind
{
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Freehand,
    Highlight,
    Text,
    Blur,
    Pixelate,
    Crop,
    SpeechBalloon,
    Callout,
    StepNumber,
    StepAlpha,
    StepRoman,
    CursorStamp,
    ClickStamp,
    Emoji,
    Magnify,
    Spotlight,
    CurvedLine,
    CurvedArrow,
    Bracket
}

public enum AnnotationLineStyle
{
    Solid,
    Dash,
    Dot
}

public enum AnnotationTextAlignment
{
    Left,
    Center,
    Right
}

public enum AnnotationAlignment
{
    Left,
    Right,
    Top,
    Bottom,
    CenterHorizontal,
    CenterVertical
}

public enum AnnotationMatchSize
{
    Width,
    Height,
    Both
}

public enum AnnotationDistribution
{
    Horizontal,
    Vertical
}

public sealed record AnnotationStyleUpdate(
    uint? Argb = null,
    float? StrokeWidth = null,
    float? Opacity = null,
    AnnotationLineStyle? LineStyle = null,
    uint? FillArgb = null,
    bool ClearFill = false,
    string? FontFamily = null,
    float? FontSize = null,
    bool? FontBold = null,
    bool? FontItalic = null,
    AnnotationTextAlignment? TextAlignment = null);

public sealed record AnnotationLayer(
    AnnotationKind Kind,
    PixelRect Bounds,
    IReadOnlyList<PixelPoint>? Points = null,
    uint Argb = 0xFFFF3B30,
    float StrokeWidth = 3f,
    string? Text = null,
    float FontSize = 18f)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? GroupId { get; init; }
    public bool IsVisible { get; init; } = true;
    public bool IsLocked { get; init; }
    public float Opacity { get; init; } = 1f;
    public double RotationDegrees { get; init; }
    public uint? FillArgb { get; init; }
    public AnnotationLineStyle LineStyle { get; init; } = AnnotationLineStyle.Solid;
    public string FontFamily { get; init; } = "Segoe UI";
    public bool FontBold { get; init; }
    public bool FontItalic { get; init; }
    public AnnotationTextAlignment TextAlignment { get; init; } = AnnotationTextAlignment.Left;
}

public sealed record AnnotationDocument(IReadOnlyList<AnnotationLayer> Layers)
{
    public static AnnotationDocument Empty => new([]);
}
