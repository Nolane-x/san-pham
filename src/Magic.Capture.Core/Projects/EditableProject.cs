using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Ocr;
using Magic.Capture.Core.ScreenGraph;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Projects;

public sealed record EditableProjectManifest(
    int SchemaVersion,
    string Product,
    Guid ProjectId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    int Width,
    int Height,
    AnnotationDocument Annotations,
    OcrDocument? Ocr = null,
    DetectedTable? Table = null,
    ScreenGraphDocument? ScreenGraph = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string ProductName = "Magic Capture Desktop";

    public static EditableProjectManifest Create(int width, int height, AnnotationDocument? annotations = null) =>
        new(CurrentSchemaVersion, ProductName, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            width, height, annotations ?? AnnotationDocument.Empty);
}

public sealed record EditableProjectValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public static class EditableProjectValidator
{
    public const long MaxPixelCount = 150_000_000;
    public const int MaxAnnotationLayers = 10_000;
    public const int MaxPointsPerLayer = 100_000;
    public const int MaxAnnotationTextLength = 100_000;
    public const int MaxMetadataEntries = 256;
    public const int MaxMetadataKeyLength = 128;
    public const int MaxMetadataValueLength = 8_192;
    public const int MaxValidationErrors = 128;

    public const int MaxOcrDocumentTextLength = 2_000_000;
    public const int MaxOcrLines = 20_000;
    public const int MaxOcrWords = 200_000;
    public const int MaxOcrLineTextLength = 100_000;
    public const int MaxOcrWordTextLength = 16_384;

    public const int MaxTableRows = 10_000;
    public const int MaxTableColumns = 4_096;
    public const int MaxTableCells = 200_000;
    public const int MaxTableCellTextLength = 100_000;
    public const int MaxTableTotalCharacters = 4_000_000;

    public const int MaxScreenGraphNodes = 50_000;
    public const int MaxScreenGraphAttributesPerNode = 64;
    public const int MaxScreenGraphAttributesTotal = 200_000;
    public const int MaxScreenGraphNodeTextLength = 100_000;
    public const int MaxScreenGraphAttributeKeyLength = 128;
    public const int MaxScreenGraphAttributeValueLength = 8_192;

    public static EditableProjectValidationResult Validate(EditableProjectManifest? manifest)
    {
        if (manifest is null) return new(false, ["Project manifest is required."]);
        var errors = new List<string>();
        if (manifest.SchemaVersion != EditableProjectManifest.CurrentSchemaVersion)
            AddError(errors, $"Unsupported project schema version: {manifest.SchemaVersion}.");
        if (!string.Equals(manifest.Product, EditableProjectManifest.ProductName, StringComparison.Ordinal))
            AddError(errors, "Project product identity does not match Magic Capture Desktop.");
        if (manifest.ProjectId == Guid.Empty) AddError(errors, "Project id is required.");
        if (manifest.Width <= 0 || manifest.Height <= 0) AddError(errors, "Project dimensions must be positive.");
        if (manifest.Width > 200_000 || manifest.Height > 200_000) AddError(errors, "Project dimensions exceed the supported safety limit.");
        if (manifest.Width > 0 && manifest.Height > 0 && (long)manifest.Width * manifest.Height > MaxPixelCount)
            AddError(errors, $"Project pixel area exceeds the supported safety limit of {MaxPixelCount:N0} pixels.");
        if (manifest.Annotations is null)
        {
            AddError(errors, "Project annotations are required.");
        }
        else
        {
            ValidateAnnotations(manifest.Annotations, errors);
        }

        ValidateOcr(manifest.Ocr, errors);
        ValidateTable(manifest.Table, errors);
        ValidateScreenGraph(manifest.ScreenGraph, manifest.Width, manifest.Height, errors);
        ValidateMetadata(manifest.Metadata, errors);
        return new(errors.Count == 0, errors);
    }

    private static void ValidateAnnotations(AnnotationDocument document, List<string> errors)
    {
        if (document.Layers is null)
        {
            AddError(errors, "Project annotation layer collection is required.");
            return;
        }
        if (document.Layers.Count > MaxAnnotationLayers)
        {
            AddError(errors, $"Project annotation count exceeds the supported limit of {MaxAnnotationLayers:N0} layers.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in document.Layers)
        {
            if (errors.Count >= MaxValidationErrors) return;
            if (layer is null)
            {
                AddError(errors, "Project contains a null annotation layer.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(layer.Id) || layer.Id.Length > 128)
                AddError(errors, "Project annotation layer id is invalid.");
            else if (!ids.Add(layer.Id))
                AddError(errors, $"Project contains duplicate annotation layer id '{layer.Id}'.");
            if (!Enum.IsDefined(typeof(AnnotationKind), layer.Kind)) AddError(errors, "Project annotation kind is invalid.");
            if (!Enum.IsDefined(typeof(AnnotationLineStyle), layer.LineStyle)) AddError(errors, "Project annotation line style is invalid.");
            if (!Enum.IsDefined(typeof(AnnotationTextAlignment), layer.TextAlignment)) AddError(errors, "Project annotation text alignment is invalid.");
            if (layer.GroupId is { Length: > 128 } || (layer.GroupId is not null && string.IsNullOrWhiteSpace(layer.GroupId)))
                AddError(errors, $"Project annotation '{layer.Id}' has an invalid group id.");
            if (!float.IsFinite(layer.StrokeWidth) || layer.StrokeWidth is < 0.25f or > 256f)
                AddError(errors, $"Project annotation '{layer.Id}' has an invalid stroke width.");
            if (!float.IsFinite(layer.FontSize) || layer.FontSize is < 1f or > 512f)
                AddError(errors, $"Project annotation '{layer.Id}' has an invalid font size.");
            if (!float.IsFinite(layer.Opacity) || layer.Opacity is < 0f or > 1f)
                AddError(errors, $"Project annotation '{layer.Id}' has invalid opacity.");
            if (!double.IsFinite(layer.RotationDegrees) || Math.Abs(layer.RotationDegrees) > 1_000_000d)
                AddError(errors, $"Project annotation '{layer.Id}' has invalid rotation.");
            if (!IsSaneBounds(layer.Bounds)) AddError(errors, $"Project annotation '{layer.Id}' has unsafe bounds.");
            if (layer.Points is { Count: > MaxPointsPerLayer })
                AddError(errors, $"Project annotation '{layer.Id}' contains too many points.");
            else if (layer.Points is not null && layer.Points.Any(point => Math.Abs((long)point.X) > 1_000_000 || Math.Abs((long)point.Y) > 1_000_000))
                AddError(errors, $"Project annotation '{layer.Id}' contains unsafe point coordinates.");
            if (layer.Text is { Length: > MaxAnnotationTextLength })
                AddError(errors, $"Project annotation '{layer.Id}' contains too much text.");
            if (layer.FontFamily is { Length: > 128 })
                AddError(errors, $"Project annotation '{layer.Id}' font family name is too long.");
        }
    }

    private static void ValidateOcr(OcrDocument? ocr, List<string> errors)
    {
        if (ocr is null) return;
        if (ocr.Text is null || ocr.Text.Length > MaxOcrDocumentTextLength)
            AddError(errors, $"Project OCR text exceeds the supported limit of {MaxOcrDocumentTextLength:N0} characters.");
        if (ocr.TextAngleRadians is { } angle && !double.IsFinite(angle))
            AddError(errors, "Project OCR text angle is invalid.");
        if (ocr.Lines is null)
        {
            AddError(errors, "Project OCR line collection is invalid.");
            return;
        }
        if (ocr.Lines.Count > MaxOcrLines)
        {
            AddError(errors, $"Project OCR contains more than {MaxOcrLines:N0} lines.");
            return;
        }

        var wordCount = 0;
        foreach (var line in ocr.Lines)
        {
            if (errors.Count >= MaxValidationErrors) return;
            if (line is null)
            {
                AddError(errors, "Project OCR contains a null line.");
                continue;
            }
            if (line.Text is null || line.Text.Length > MaxOcrLineTextLength)
                AddError(errors, "Project OCR contains an oversized line.");
            if (!IsSaneBounds(line.Bounds)) AddError(errors, "Project OCR line has unsafe bounds.");
            if (line.Words is null)
            {
                AddError(errors, "Project OCR line has an invalid word collection.");
                continue;
            }
            wordCount += line.Words.Count;
            if (wordCount > MaxOcrWords)
            {
                AddError(errors, $"Project OCR contains more than {MaxOcrWords:N0} words.");
                return;
            }
            foreach (var word in line.Words)
            {
                if (word is null)
                {
                    AddError(errors, "Project OCR contains a null word.");
                    continue;
                }
                if (word.Text is null || word.Text.Length > MaxOcrWordTextLength)
                    AddError(errors, "Project OCR contains an oversized word.");
                if (!IsSaneBounds(word.Bounds)) AddError(errors, "Project OCR word has unsafe bounds.");
                if (errors.Count >= MaxValidationErrors) return;
            }
        }
    }

    private static void ValidateTable(DetectedTable? table, List<string> errors)
    {
        if (table is null) return;
        if (table.Rows is null)
        {
            AddError(errors, "Project table row collection is invalid.");
            return;
        }
        if (table.Rows.Count > MaxTableRows)
        {
            AddError(errors, $"Project table contains more than {MaxTableRows:N0} rows.");
            return;
        }
        if (table.ColumnCount <= 0 || table.ColumnCount > MaxTableColumns)
            AddError(errors, $"Project table column count exceeds the supported limit of {MaxTableColumns:N0}.");
        if (table.RowCount != table.Rows.Count)
            AddError(errors, "Project table row count does not match its row collection.");
        if (!double.IsFinite(table.Confidence) || table.Confidence is < 0d or > 1d)
            AddError(errors, "Project table confidence is invalid.");
        if (!IsSaneBounds(table.Bounds)) AddError(errors, "Project table has unsafe bounds.");

        var cellCount = 0;
        long totalCharacters = 0;
        foreach (var row in table.Rows)
        {
            if (row is null)
            {
                AddError(errors, "Project table contains a null row.");
                continue;
            }
            if (row.Count > MaxTableColumns)
            {
                AddError(errors, "Project table row contains too many cells.");
                return;
            }
            cellCount += row.Count;
            if (cellCount > MaxTableCells)
            {
                AddError(errors, $"Project table contains more than {MaxTableCells:N0} cells.");
                return;
            }
            foreach (var cell in row)
            {
                if (cell is null)
                {
                    AddError(errors, "Project table contains a null cell.");
                    continue;
                }
                if (cell.Length > MaxTableCellTextLength)
                    AddError(errors, "Project table contains an oversized cell.");
                totalCharacters += cell.Length;
                if (totalCharacters > MaxTableTotalCharacters)
                {
                    AddError(errors, $"Project table text exceeds the supported limit of {MaxTableTotalCharacters:N0} characters.");
                    return;
                }
            }
            if (errors.Count >= MaxValidationErrors) return;
        }
    }

    private static void ValidateScreenGraph(ScreenGraphDocument? graph, int projectWidth, int projectHeight, List<string> errors)
    {
        if (graph is null) return;
        if (graph.SchemaVersion != ScreenGraphBuilder.CurrentSchemaVersion)
            AddError(errors, $"Project ScreenGraph schema version {graph.SchemaVersion} is unsupported.");
        if (graph.CaptureId == Guid.Empty) AddError(errors, "Project ScreenGraph capture id is required.");
        if (graph.Width != projectWidth || graph.Height != projectHeight)
            AddError(errors, "Project ScreenGraph dimensions do not match the base image.");
        if (string.IsNullOrWhiteSpace(graph.SourceKind) || graph.SourceKind.Length > 128)
            AddError(errors, "Project ScreenGraph source kind is invalid.");
        if (graph.SourceDisplayName is { Length: > 2_048 })
            AddError(errors, "Project ScreenGraph source display name is too long.");
        if (graph.Nodes is null)
        {
            AddError(errors, "Project ScreenGraph node collection is invalid.");
            return;
        }
        if (graph.Nodes.Count > MaxScreenGraphNodes)
        {
            AddError(errors, $"Project ScreenGraph contains more than {MaxScreenGraphNodes:N0} nodes.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var totalAttributes = 0;
        foreach (var node in graph.Nodes)
        {
            if (errors.Count >= MaxValidationErrors) return;
            if (node is null)
            {
                AddError(errors, "Project ScreenGraph contains a null node.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 128)
                AddError(errors, "Project ScreenGraph node id is invalid.");
            else if (!ids.Add(node.Id))
                AddError(errors, $"Project ScreenGraph contains duplicate node id '{node.Id}'.");
            if (!Enum.IsDefined(typeof(ScreenNodeKind), node.Kind)) AddError(errors, "Project ScreenGraph node kind is invalid.");
            if (node.Text is { Length: > MaxScreenGraphNodeTextLength }) AddError(errors, "Project ScreenGraph node text is too long.");
            if (!IsSaneBounds(node.Bounds)) AddError(errors, "Project ScreenGraph node has unsafe bounds.");
            if (!double.IsFinite(node.Confidence) || node.Confidence is < 0d or > 1d) AddError(errors, "Project ScreenGraph node confidence is invalid.");
            if (node.ParentId is { Length: > 128 }) AddError(errors, "Project ScreenGraph parent id is too long.");
            if (node.Attributes is null) continue;
            if (node.Attributes.Count > MaxScreenGraphAttributesPerNode)
            {
                AddError(errors, $"Project ScreenGraph node contains more than {MaxScreenGraphAttributesPerNode:N0} attributes.");
                continue;
            }
            totalAttributes += node.Attributes.Count;
            if (totalAttributes > MaxScreenGraphAttributesTotal)
            {
                AddError(errors, $"Project ScreenGraph contains more than {MaxScreenGraphAttributesTotal:N0} attributes.");
                return;
            }
            foreach (var pair in node.Attributes)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > MaxScreenGraphAttributeKeyLength || pair.Value is null || pair.Value.Length > MaxScreenGraphAttributeValueLength)
                {
                    AddError(errors, "Project ScreenGraph contains an invalid attribute.");
                    break;
                }
            }
        }
    }

    private static bool IsSaneBounds(Magic.Capture.Core.Geometry.PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 200_000 || bounds.Height > 200_000) return false;
        if (Math.Abs((long)bounds.X) > 1_000_000 || Math.Abs((long)bounds.Y) > 1_000_000) return false;
        var right = (long)bounds.X + bounds.Width;
        var bottom = (long)bounds.Y + bounds.Height;
        return Math.Abs(right) <= 1_200_000 && Math.Abs(bottom) <= 1_200_000;
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, string>? metadata, List<string> errors)
    {
        if (metadata is null) return;
        if (metadata.Count > MaxMetadataEntries)
        {
            AddError(errors, $"Project metadata exceeds the supported limit of {MaxMetadataEntries:N0} entries.");
            return;
        }
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > MaxMetadataKeyLength || pair.Value is null || pair.Value.Length > MaxMetadataValueLength)
            {
                AddError(errors, "Project metadata contains an invalid key or value.");
                return;
            }
        }
    }

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count < MaxValidationErrors) errors.Add(message);
    }
}
