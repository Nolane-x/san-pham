using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Privacy;

public enum RedactionStyle
{
    Pixelate,
    Blur
}

public static class RedactionPlanner
{
    public static AnnotationDocument Create(
        IEnumerable<SensitiveFinding> findings,
        PixelRect imageBounds,
        RedactionStyle style = RedactionStyle.Pixelate,
        int padding = 3)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var layers = new List<AnnotationLayer>();
        foreach (var finding in findings)
        {
            if (finding.Bounds.IsEmpty) continue;
            var expanded = Expand(finding.Bounds, Math.Clamp(padding, 0, 32)).Intersect(imageBounds);
            if (expanded.IsEmpty) continue;
            layers.Add(new AnnotationLayer(style == RedactionStyle.Blur ? AnnotationKind.Blur : AnnotationKind.Pixelate, expanded)
            {
                Id = $"redact-{finding.SourceNodeId ?? Guid.NewGuid().ToString("N")}-{layers.Count + 1}"
            });
        }
        return new AnnotationDocument(layers);
    }

    private static PixelRect Expand(PixelRect rect, int padding) =>
        new(rect.X - padding, rect.Y - padding, rect.Width + padding * 2, rect.Height + padding * 2);
}
