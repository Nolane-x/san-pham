using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class AnnotationDocumentTests
{
    [Fact]
    public void Move_updates_bounds_and_freehand_points_without_changing_identity()
    {
        var layer = new AnnotationLayer(AnnotationKind.Freehand, new PixelRect(10, 10, 20, 20), [new PixelPoint(10, 10), new PixelPoint(20, 20)]);
        var document = new AnnotationDocument([layer]);
        var moved = AnnotationDocumentEditor.Move(document, layer.Id, 5, -2);
        var result = Assert.Single(moved.Layers);
        Assert.Equal(layer.Id, result.Id);
        Assert.Equal(new PixelRect(15, 8, 20, 20), result.Bounds);
        Assert.Equal(new PixelPoint(15, 8), result.Points![0]);
    }

    [Fact]
    public void Locked_layer_rejects_mutation()
    {
        var layer = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(1, 1, 10, 10)) { IsLocked = true };
        var document = new AnnotationDocument([layer]);
        Assert.Throws<InvalidOperationException>(() => AnnotationDocumentEditor.Resize(document, layer.Id, new PixelRect(2, 2, 20, 20)));
    }

    [Fact]
    public void Duplicate_gets_new_identity_and_is_inserted_above_source()
    {
        var layer = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(1, 1, 10, 10));
        var document = new AnnotationDocument([layer]);
        var duplicated = AnnotationDocumentEditor.Duplicate(document, layer.Id, 4, 4);
        Assert.Equal(2, duplicated.Layers.Count);
        Assert.NotEqual(layer.Id, duplicated.Layers[1].Id);
        Assert.Equal(new PixelRect(5, 5, 10, 10), duplicated.Layers[1].Bounds);
    }

    [Fact]
    public void Z_order_operations_are_stable_at_boundaries()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(0, 0, 1, 1));
        var b = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(1, 1, 1, 1));
        var c = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(2, 2, 1, 1));
        var document = new AnnotationDocument([a, b, c]);
        var front = AnnotationDocumentEditor.BringToFront(document, a.Id);
        Assert.Equal([b.Id, c.Id, a.Id], front.Layers.Select(x => x.Id));
        var back = AnnotationDocumentEditor.SendToBack(front, a.Id);
        Assert.Equal([a.Id, b.Id, c.Id], back.Layers.Select(x => x.Id));
    }

    [Fact]
    public void Lock_and_rotation_are_explicit_document_edits()
    {
        var layer = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(1, 1, 10, 10));
        var document = new AnnotationDocument([layer]);
        var rotated = AnnotationDocumentEditor.SetRotation(document, layer.Id, 450);
        Assert.Equal(90, rotated.Layers[0].RotationDegrees);
        var locked = AnnotationDocumentEditor.SetLocked(rotated, layer.Id, true);
        Assert.True(locked.Layers[0].IsLocked);
    }

    [Fact]
    public void Resize_scales_freehand_points_with_bounds()
    {
        var layer = new AnnotationLayer(AnnotationKind.Freehand, new PixelRect(10, 10, 20, 20),
            [new PixelPoint(10, 10), new PixelPoint(20, 20), new PixelPoint(30, 30)]);
        var resized = AnnotationDocumentEditor.Resize(new AnnotationDocument([layer]), layer.Id, new PixelRect(20, 30, 40, 10));
        var result = Assert.Single(resized.Layers);
        Assert.Equal(new PixelPoint(20, 30), result.Points![0]);
        Assert.Equal(new PixelPoint(40, 35), result.Points[1]);
        Assert.Equal(new PixelPoint(60, 40), result.Points[2]);
    }

    [Fact]
    public void Group_and_ungroup_apply_to_all_selected_layers()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(0, 0, 10, 10));
        var b = new AnnotationLayer(AnnotationKind.Ellipse, new PixelRect(20, 0, 10, 10));
        var grouped = AnnotationDocumentEditor.Group(new AnnotationDocument([a, b]), [a.Id, b.Id]);
        Assert.False(string.IsNullOrWhiteSpace(grouped.Layers[0].GroupId));
        Assert.Equal(grouped.Layers[0].GroupId, grouped.Layers[1].GroupId);
        var ungrouped = AnnotationDocumentEditor.Ungroup(grouped, [a.Id, b.Id]);
        Assert.All(ungrouped.Layers, layer => Assert.Null(layer.GroupId));
    }

    [Fact]
    public void Align_and_match_size_use_first_selected_layer_as_anchor()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(10, 20, 30, 40));
        var b = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(80, 90, 12, 14));
        var doc = new AnnotationDocument([a, b]);
        var aligned = AnnotationDocumentEditor.Align(doc, [a.Id, b.Id], AnnotationAlignment.Left);
        Assert.Equal(10, aligned.Layers[1].Bounds.X);
        var sized = AnnotationDocumentEditor.MatchSize(aligned, [a.Id, b.Id], AnnotationMatchSize.Both);
        Assert.Equal(30, sized.Layers[1].Bounds.Width);
        Assert.Equal(40, sized.Layers[1].Bounds.Height);
    }

    [Fact]
    public void Distribute_horizontal_keeps_outer_layers_and_spaces_centers_evenly()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(0, 0, 10, 10));
        var b = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(70, 0, 10, 10));
        var c = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(30, 0, 10, 10));
        var result = AnnotationDocumentEditor.Distribute(new AnnotationDocument([a, b, c]), [a.Id, b.Id, c.Id], AnnotationDistribution.Horizontal);
        Assert.Equal(0, result.Layers.Single(x => x.Id == a.Id).Bounds.X);
        Assert.Equal(70, result.Layers.Single(x => x.Id == b.Id).Bounds.X);
        Assert.Equal(35, result.Layers.Single(x => x.Id == c.Id).Bounds.X);
    }

    [Fact]
    public void Set_style_updates_multiple_layers_and_preserves_unset_fields()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(0, 0, 10, 10)) { Argb = 0xFF010203, StrokeWidth = 2 };
        var b = new AnnotationLayer(AnnotationKind.Text, new PixelRect(20, 0, 10, 10)) { FontFamily = "Segoe UI" };
        var result = AnnotationDocumentEditor.SetStyle(new AnnotationDocument([a, b]), [a.Id, b.Id], new AnnotationStyleUpdate(
            Opacity: .5f, LineStyle: AnnotationLineStyle.Dash, FontFamily: "Consolas", FontBold: true, TextAlignment: AnnotationTextAlignment.Center));
        Assert.All(result.Layers, layer => Assert.Equal(.5f, layer.Opacity));
        Assert.Equal(0xFF010203u, result.Layers[0].Argb);
        Assert.Equal(2, result.Layers[0].StrokeWidth);
        Assert.Equal("Consolas", result.Layers[1].FontFamily);
        Assert.True(result.Layers[1].FontBold);
        Assert.Equal(AnnotationTextAlignment.Center, result.Layers[1].TextAlignment);
    }

    [Fact]
    public void Duplicate_many_creates_fresh_ids_and_preserves_group_relationship_inside_copy()
    {
        var a = new AnnotationLayer(AnnotationKind.Rectangle, new PixelRect(0, 0, 10, 10)) { GroupId = "g" };
        var b = new AnnotationLayer(AnnotationKind.Ellipse, new PixelRect(20, 0, 10, 10)) { GroupId = "g" };
        var result = AnnotationDocumentEditor.DuplicateMany(new AnnotationDocument([a, b]), [a.Id, b.Id], 5, 5);
        Assert.Equal(4, result.Layers.Count);
        var copies = result.Layers.Skip(2).ToArray();
        Assert.All(copies, copy => Assert.DoesNotContain(copy.Id, new[] { a.Id, b.Id }));
        Assert.False(string.IsNullOrWhiteSpace(copies[0].GroupId));
        Assert.Equal(copies[0].GroupId, copies[1].GroupId);
        Assert.NotEqual("g", copies[0].GroupId);
    }

}
