using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Annotation;

public static class AnnotationDocumentEditor
{
    public static AnnotationDocument Move(AnnotationDocument document, string layerId, int deltaX, int deltaY)
    {
        ArgumentNullException.ThrowIfNull(document);
        var (layers, index, layer) = FindMutable(document, layerId);
        layers[index] = Translate(layer, deltaX, deltaY);
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument MoveMany(AnnotationDocument document, IEnumerable<string> layerIds, int deltaX, int deltaY)
    {
        var ids = NormalizeIds(layerIds);
        if (ids.Count == 0) return document;
        var layers = document.Layers.ToList();
        foreach (var id in ids)
        {
            var index = FindIndex(layers, id);
            EnsureMutable(layers[index]);
            layers[index] = Translate(layers[index], deltaX, deltaY);
        }
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument Resize(AnnotationDocument document, string layerId, PixelRect bounds)
    {
        if (bounds.IsEmpty) throw new ArgumentException("Annotation bounds must not be empty.", nameof(bounds));
        var (layers, index, layer) = FindMutable(document, layerId);
        layers[index] = ResizeLayer(layer, bounds);
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument Remove(AnnotationDocument document, string layerId)
    {
        var (layers, index, layer) = FindMutable(document, layerId);
        _ = layer;
        layers.RemoveAt(index);
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument RemoveMany(AnnotationDocument document, IEnumerable<string> layerIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = NormalizeIds(layerIds);
        if (ids.Count == 0) return document;
        foreach (var id in ids)
        {
            var layer = document.Layers.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Annotation layer '{id}' was not found.");
            EnsureMutable(layer);
        }
        return new AnnotationDocument(document.Layers.Where(layer => !ids.Contains(layer.Id)).ToArray());
    }

    public static AnnotationDocument Duplicate(AnnotationDocument document, string layerId, int offsetX = 8, int offsetY = 8)
    {
        var (layers, index, layer) = FindMutable(document, layerId);
        var copy = CloneWithOffset(layer, offsetX, offsetY, null);
        layers.Insert(index + 1, copy);
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument DuplicateMany(AnnotationDocument document, IEnumerable<string> layerIds, int offsetX = 8, int offsetY = 8)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = NormalizeIds(layerIds);
        if (ids.Count == 0) return document;

        var selected = document.Layers.Where(layer => ids.Contains(layer.Id)).ToArray();
        if (selected.Length != ids.Count) throw new KeyNotFoundException("One or more annotation layers were not found.");
        foreach (var layer in selected) EnsureMutable(layer);

        var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var copies = new List<AnnotationLayer>(selected.Length);
        foreach (var layer in selected)
        {
            string? copiedGroup = null;
            if (!string.IsNullOrWhiteSpace(layer.GroupId))
            {
                if (!groupMap.TryGetValue(layer.GroupId!, out copiedGroup))
                {
                    copiedGroup = Guid.NewGuid().ToString("N");
                    groupMap[layer.GroupId!] = copiedGroup;
                }
            }
            copies.Add(CloneWithOffset(layer, offsetX, offsetY, copiedGroup));
        }

        var layers = document.Layers.ToList();
        layers.AddRange(copies);
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument AppendCopies(AnnotationDocument document, IEnumerable<AnnotationLayer> sourceLayers, int offsetX = 8, int offsetY = 8)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceLayers);
        var source = sourceLayers.ToArray();
        if (source.Length == 0) return document;
        var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var copies = new List<AnnotationLayer>(source.Length);
        foreach (var layer in source)
        {
            string? copiedGroup = null;
            if (!string.IsNullOrWhiteSpace(layer.GroupId))
            {
                if (!groupMap.TryGetValue(layer.GroupId!, out copiedGroup))
                {
                    copiedGroup = Guid.NewGuid().ToString("N");
                    groupMap[layer.GroupId!] = copiedGroup;
                }
            }
            copies.Add(CloneWithOffset(layer, offsetX, offsetY, copiedGroup));
        }
        var layers = document.Layers.ToList();
        layers.AddRange(copies);
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument BringForward(AnnotationDocument document, string layerId) => MoveZ(document, layerId, +1, false);
    public static AnnotationDocument SendBackward(AnnotationDocument document, string layerId) => MoveZ(document, layerId, -1, false);
    public static AnnotationDocument BringToFront(AnnotationDocument document, string layerId) => MoveZ(document, layerId, +1, true);
    public static AnnotationDocument SendToBack(AnnotationDocument document, string layerId) => MoveZ(document, layerId, -1, true);

    public static AnnotationDocument SetVisibility(AnnotationDocument document, string layerId, bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layers = document.Layers.ToList();
        var index = FindIndex(layers, layerId);
        layers[index] = layers[index] with { IsVisible = isVisible };
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument SetLocked(AnnotationDocument document, string layerId, bool isLocked)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layers = document.Layers.ToList();
        var index = FindIndex(layers, layerId);
        layers[index] = layers[index] with { IsLocked = isLocked };
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument SetRotation(AnnotationDocument document, string layerId, double degrees)
    {
        var (layers, index, layer) = FindMutable(document, layerId);
        var normalized = NormalizeRotation(degrees);
        layers[index] = layer with { RotationDegrees = normalized };
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument Group(AnnotationDocument document, IEnumerable<string> layerIds)
    {
        var ids = NormalizeIds(layerIds);
        if (ids.Count < 2) throw new ArgumentException("Select at least two layers to group.", nameof(layerIds));
        var layers = document.Layers.ToList();
        var groupId = Guid.NewGuid().ToString("N");
        foreach (var id in ids)
        {
            var index = FindIndex(layers, id);
            EnsureMutable(layers[index]);
            layers[index] = layers[index] with { GroupId = groupId };
        }
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument Ungroup(AnnotationDocument document, IEnumerable<string> layerIds)
    {
        var ids = NormalizeIds(layerIds);
        if (ids.Count == 0) return document;
        var layers = document.Layers.ToList();
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var index = FindIndex(layers, id);
            EnsureMutable(layers[index]);
            if (!string.IsNullOrWhiteSpace(layers[index].GroupId)) groups.Add(layers[index].GroupId!);
        }
        if (groups.Count == 0) return document;
        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i].GroupId is { } groupId && groups.Contains(groupId))
            {
                EnsureMutable(layers[i]);
                layers[i] = layers[i] with { GroupId = null };
            }
        }
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument Align(AnnotationDocument document, IEnumerable<string> layerIds, AnnotationAlignment alignment)
    {
        var ids = NormalizeIds(layerIds);
        if (ids.Count < 2) throw new ArgumentException("Select at least two layers to align.", nameof(layerIds));
        var layers = document.Layers.ToList();
        var anchor = layers[FindIndex(layers, ids[0])];
        EnsureMutable(anchor);
        foreach (var id in ids.Skip(1))
        {
            var index = FindIndex(layers, id);
            var layer = layers[index];
            EnsureMutable(layer);
            var bounds = layer.Bounds;
            var target = alignment switch
            {
                AnnotationAlignment.Left => new PixelRect(anchor.Bounds.X, bounds.Y, bounds.Width, bounds.Height),
                AnnotationAlignment.Right => new PixelRect(anchor.Bounds.Right - bounds.Width, bounds.Y, bounds.Width, bounds.Height),
                AnnotationAlignment.Top => new PixelRect(bounds.X, anchor.Bounds.Y, bounds.Width, bounds.Height),
                AnnotationAlignment.Bottom => new PixelRect(bounds.X, anchor.Bounds.Bottom - bounds.Height, bounds.Width, bounds.Height),
                AnnotationAlignment.CenterHorizontal => new PixelRect(anchor.Bounds.Center.X - bounds.Width / 2, bounds.Y, bounds.Width, bounds.Height),
                AnnotationAlignment.CenterVertical => new PixelRect(bounds.X, anchor.Bounds.Center.Y - bounds.Height / 2, bounds.Width, bounds.Height),
                _ => bounds
            };
            layers[index] = Translate(layer, target.X - bounds.X, target.Y - bounds.Y);
        }
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument MatchSize(AnnotationDocument document, IEnumerable<string> layerIds, AnnotationMatchSize match)
    {
        var ids = NormalizeIds(layerIds);
        if (ids.Count < 2) throw new ArgumentException("Select at least two layers to match size.", nameof(layerIds));
        var layers = document.Layers.ToList();
        var anchor = layers[FindIndex(layers, ids[0])];
        EnsureMutable(anchor);
        foreach (var id in ids.Skip(1))
        {
            var index = FindIndex(layers, id);
            var layer = layers[index];
            EnsureMutable(layer);
            var width = match is AnnotationMatchSize.Width or AnnotationMatchSize.Both ? anchor.Bounds.Width : layer.Bounds.Width;
            var height = match is AnnotationMatchSize.Height or AnnotationMatchSize.Both ? anchor.Bounds.Height : layer.Bounds.Height;
            layers[index] = ResizeLayer(layer, new PixelRect(layer.Bounds.X, layer.Bounds.Y, Math.Max(1, width), Math.Max(1, height)));
        }
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument Distribute(AnnotationDocument document, IEnumerable<string> layerIds, AnnotationDistribution distribution)
    {
        var ids = NormalizeIds(layerIds);
        if (ids.Count < 3) throw new ArgumentException("Select at least three layers to distribute.", nameof(layerIds));
        var layers = document.Layers.ToList();
        var selected = ids.Select(id =>
        {
            var index = FindIndex(layers, id);
            EnsureMutable(layers[index]);
            return (Index: index, Layer: layers[index]);
        }).ToArray();

        var ordered = distribution == AnnotationDistribution.Horizontal
            ? selected.OrderBy(item => item.Layer.Bounds.Center.X).ToArray()
            : selected.OrderBy(item => item.Layer.Bounds.Center.Y).ToArray();
        var firstCenter = distribution == AnnotationDistribution.Horizontal ? ordered[0].Layer.Bounds.Center.X : ordered[0].Layer.Bounds.Center.Y;
        var lastCenter = distribution == AnnotationDistribution.Horizontal ? ordered[^1].Layer.Bounds.Center.X : ordered[^1].Layer.Bounds.Center.Y;
        var spacing = (lastCenter - firstCenter) / (double)(ordered.Length - 1);
        for (var i = 1; i < ordered.Length - 1; i++)
        {
            var targetCenter = (int)Math.Round(firstCenter + spacing * i);
            var layer = ordered[i].Layer;
            var deltaX = distribution == AnnotationDistribution.Horizontal ? targetCenter - layer.Bounds.Center.X : 0;
            var deltaY = distribution == AnnotationDistribution.Vertical ? targetCenter - layer.Bounds.Center.Y : 0;
            layers[ordered[i].Index] = Translate(layer, deltaX, deltaY);
        }
        return new AnnotationDocument(layers);
    }

    public static AnnotationDocument SetStyle(AnnotationDocument document, IEnumerable<string> layerIds, AnnotationStyleUpdate update)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(update);
        var ids = NormalizeIds(layerIds);
        if (ids.Count == 0) return document;
        var layers = document.Layers.ToList();
        foreach (var id in ids)
        {
            var index = FindIndex(layers, id);
            var layer = layers[index];
            EnsureMutable(layer);
            var fontFamily = update.FontFamily is null ? layer.FontFamily : NormalizeFontFamily(update.FontFamily);
            layers[index] = layer with
            {
                Argb = update.Argb ?? layer.Argb,
                StrokeWidth = update.StrokeWidth is { } stroke ? Math.Clamp(stroke, 1f, 64f) : layer.StrokeWidth,
                Opacity = update.Opacity is { } opacity ? Math.Clamp(opacity, 0f, 1f) : layer.Opacity,
                LineStyle = update.LineStyle ?? layer.LineStyle,
                FillArgb = update.ClearFill ? null : update.FillArgb ?? layer.FillArgb,
                FontFamily = fontFamily,
                FontSize = update.FontSize is { } fontSize ? Math.Clamp(fontSize, 8f, 256f) : layer.FontSize,
                FontBold = update.FontBold ?? layer.FontBold,
                FontItalic = update.FontItalic ?? layer.FontItalic,
                TextAlignment = update.TextAlignment ?? layer.TextAlignment
            };
        }
        return new AnnotationDocument(layers);
    }

    private static AnnotationDocument MoveZ(AnnotationDocument document, string layerId, int direction, bool extreme)
    {
        var (layers, index, layer) = FindMutable(document, layerId);
        var target = extreme ? (direction > 0 ? layers.Count - 1 : 0) : Math.Clamp(index + direction, 0, layers.Count - 1);
        if (target == index) return document;
        layers.RemoveAt(index);
        layers.Insert(target, layer);
        return new AnnotationDocument(layers);
    }

    private static AnnotationLayer Translate(AnnotationLayer layer, int deltaX, int deltaY)
    {
        var points = layer.Points?.Select(point => new PixelPoint(point.X + deltaX, point.Y + deltaY)).ToArray();
        return layer with
        {
            Bounds = new PixelRect(layer.Bounds.X + deltaX, layer.Bounds.Y + deltaY, layer.Bounds.Width, layer.Bounds.Height),
            Points = points
        };
    }

    private static AnnotationLayer ResizeLayer(AnnotationLayer layer, PixelRect bounds)
    {
        IReadOnlyList<PixelPoint>? points = layer.Points;
        if (points is { Count: > 0 } && layer.Bounds.Width > 0 && layer.Bounds.Height > 0)
        {
            var scaleX = bounds.Width / (double)layer.Bounds.Width;
            var scaleY = bounds.Height / (double)layer.Bounds.Height;
            points = points.Select(point => new PixelPoint(
                bounds.X + (int)Math.Round((point.X - layer.Bounds.X) * scaleX),
                bounds.Y + (int)Math.Round((point.Y - layer.Bounds.Y) * scaleY))).ToArray();
        }
        return layer with { Bounds = bounds, Points = points };
    }

    private static AnnotationLayer CloneWithOffset(AnnotationLayer layer, int offsetX, int offsetY, string? groupId)
    {
        var translated = Translate(layer, offsetX, offsetY);
        return translated with
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            IsLocked = false
        };
    }

    private static double NormalizeRotation(double degrees)
    {
        var normalized = degrees % 360d;
        if (normalized < 0) normalized += 360d;
        return normalized;
    }

    private static string NormalizeFontFamily(string fontFamily)
    {
        var value = fontFamily.Trim();
        if (value.Length == 0) return "Segoe UI";
        return value.Length <= 120 ? value : value[..120];
    }

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string> layerIds)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in layerIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            ordered.Add(id);
        }
        return ordered;
    }

    private static (List<AnnotationLayer> Layers, int Index, AnnotationLayer Layer) FindMutable(AnnotationDocument document, string layerId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layers = document.Layers.ToList();
        var index = FindIndex(layers, layerId);
        var layer = layers[index];
        EnsureMutable(layer);
        return (layers, index, layer);
    }

    private static void EnsureMutable(AnnotationLayer layer)
    {
        if (layer.IsLocked) throw new InvalidOperationException("The annotation layer is locked.");
    }

    private static int FindIndex(IReadOnlyList<AnnotationLayer> layers, string layerId)
    {
        if (string.IsNullOrWhiteSpace(layerId)) throw new ArgumentException("Layer id is required.", nameof(layerId));
        for (var i = 0; i < layers.Count; i++)
            if (string.Equals(layers[i].Id, layerId, StringComparison.Ordinal)) return i;
        throw new KeyNotFoundException($"Annotation layer '{layerId}' was not found.");
    }
}
