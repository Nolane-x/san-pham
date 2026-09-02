using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Signals;

public enum TextSignalKind
{
    Url,
    Email,
    Phone,
    IpAddress,
    FilePath,
    StackFrame,
    ErrorHeadline,
    ErrorCode,
    Money,
    Percentage,
    LineReference,
    CodeLike
}

public sealed record TextSignal(TextSignalKind Kind, string Value, PixelRect Bounds, double Confidence);
