using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Ai;

public sealed record AiActionResult(
    string Title,
    string Markdown,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> EvidenceIds,
    string? RawJson = null);

public sealed record ResolvedEvidence(Guid CaptureId, string EvidenceId, string NodeId, ScreenNodeKind Kind, string? Text, PixelRect Bounds, double Confidence);
