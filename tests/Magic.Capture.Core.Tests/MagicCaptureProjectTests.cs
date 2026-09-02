using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Projects;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.ScreenGraph;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class MagicCaptureProjectTests
{
    [Fact]
    public void New_manifest_is_valid_and_branded_for_desktop()
    {
        var manifest = EditableProjectManifest.Create(800, 600, AnnotationDocument.Empty);
        var result = EditableProjectValidator.Validate(manifest);
        Assert.True(result.IsValid);
        Assert.Equal("Magic Capture Desktop", manifest.Product);
        Assert.Equal(1, manifest.SchemaVersion);
    }

    [Fact]
    public void Validator_rejects_unknown_schema_and_invalid_dimensions()
    {
        var manifest = EditableProjectManifest.Create(1, 1, AnnotationDocument.Empty) with { SchemaVersion = 99, Width = 0 };
        var result = EditableProjectValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("schema", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.Contains("dimensions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_dimensions_whose_pixel_area_would_be_unreasonably_large()
    {
        var manifest = EditableProjectManifest.Create(20_000, 20_000, AnnotationDocument.Empty);
        var result = EditableProjectValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("pixel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_non_finite_or_unbounded_annotation_data()
    {
        var invalidLayer = new AnnotationLayer(
            AnnotationKind.Freehand,
            new Magic.Capture.Core.Geometry.PixelRect(0, 0, 100, 100),
            Enumerable.Range(0, 100_001).Select(i => new Magic.Capture.Core.Geometry.PixelPoint(i, i)).ToArray(),
            StrokeWidth: float.NaN)
        {
            Opacity = float.PositiveInfinity,
            RotationDegrees = double.NaN
        };
        var manifest = EditableProjectManifest.Create(800, 600, new AnnotationDocument([invalidLayer]));

        var result = EditableProjectValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("annotation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_duplicate_layer_ids_and_oversized_metadata()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new Magic.Capture.Core.Geometry.PixelRect(0, 0, 10, 10)) { Id = "same" };
        var b = new AnnotationLayer(AnnotationKind.Rectangle, new Magic.Capture.Core.Geometry.PixelRect(20, 20, 10, 10)) { Id = "same" };
        var metadata = Enumerable.Range(0, 300).ToDictionary(i => $"key{i}", i => "value");
        var manifest = EditableProjectManifest.Create(800, 600, new AnnotationDocument([a, b])) with { Metadata = metadata };

        var result = EditableProjectValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_oversized_nested_ocr_payloads()
    {
        var ocr = new OcrDocument(
            new string('x', EditableProjectValidator.MaxOcrDocumentTextLength + 1),
            [],
            null);
        var manifest = EditableProjectManifest.Create(800, 600, AnnotationDocument.Empty) with { Ocr = ocr };

        var result = EditableProjectValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("OCR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_oversized_table_payloads()
    {
        var rows = Enumerable.Range(0, EditableProjectValidator.MaxTableRows + 1)
            .Select(_ => (IReadOnlyList<string>)new[] { "cell" })
            .ToArray();
        var table = new DetectedTable(rows, 1, rows.Length, .9, new PixelRect(0, 0, 100, 100));
        var manifest = EditableProjectManifest.Create(800, 600, AnnotationDocument.Empty) with { Table = table };

        var result = EditableProjectValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("table", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_oversized_or_mismatched_screen_graph_payloads()
    {
        var nodes = Enumerable.Range(0, EditableProjectValidator.MaxScreenGraphNodes + 1)
            .Select(i => new ScreenGraphNode($"n{i}", ScreenNodeKind.Word, "x", new PixelRect(0, 0, 1, 1), .9, "doc", null))
            .ToArray();
        var graph = new ScreenGraphDocument(
            ScreenGraphBuilder.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            801,
            600,
            "Region",
            null,
            nodes);
        var manifest = EditableProjectManifest.Create(800, 600, AnnotationDocument.Empty) with { ScreenGraph = graph };

        var result = EditableProjectValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("ScreenGraph", StringComparison.OrdinalIgnoreCase));
    }

}
