using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.ScreenGraph;

public enum ScreenNodeKind
{
    Document,
    TextLine,
    Word,
    Table,
    Barcode,
    Url,
    Email,
    Phone,
    IpAddress,
    FilePath,
    StackFrame,
    Error,
    ErrorCode,
    Money,
    Percentage,
    LineReference,
    CodeLike,
    UiAutomation
}

public sealed record ScreenGraphNode(
    string Id,
    ScreenNodeKind Kind,
    string? Text,
    PixelRect Bounds,
    double Confidence,
    string? ParentId,
    IReadOnlyDictionary<string, string>? Attributes);

public sealed record ScreenGraphDocument(
    int SchemaVersion,
    Guid CaptureId,
    DateTimeOffset CreatedUtc,
    int Width,
    int Height,
    string SourceKind,
    string? SourceDisplayName,
    IReadOnlyList<ScreenGraphNode> Nodes)
{
    public ScreenGraphNode? Find(string id) => Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
}

public sealed record ScreenBarcode(string Format, string Value, PixelRect Bounds);

public sealed record ScreenUiAutomationNode(
    string StableKey,
    string ControlType,
    string? Name,
    string? AutomationId,
    string? Value,
    bool? IsEnabled,
    bool? IsChecked,
    bool? IsSelected,
    bool? HasKeyboardFocus,
    PixelRect Bounds,
    string? ParentStableKey,
    string? AccessKey,
    string? ProcessName,
    string? WindowTitle,
    int? ProcessId = null,
    string? AcceleratorKey = null,
    bool? IsPassword = null);

public sealed record ScreenGraphBuildInput(
    Guid CaptureId,
    DateTimeOffset CreatedUtc,
    string SourceKind,
    string? SourceDisplayName,
    int Width,
    int Height,
    PixelRect CaptureBounds,
    OcrDocument Ocr,
    DetectedTable? Table,
    IReadOnlyList<ScreenBarcode> Barcodes,
    IReadOnlyList<ScreenUiAutomationNode>? UiAutomationNodes = null);
