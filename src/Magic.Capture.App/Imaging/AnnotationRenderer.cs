using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Magic.Capture.Core.Annotation;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed class AnnotationRenderer
{
    public byte[] Render(byte[] sourcePng, AnnotationDocument document)
    {
        using var source = BitmapCodec.DecodeForPixelProcessing(sourcePng);
        using var canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        // Do not keep a Graphics object alive while LockBits-based effects run. GDI+ can reject
        // concurrent access to the same bitmap with "object is currently in use elsewhere".
        foreach (var layer in document.Layers.Where(layer => layer.Kind != AnnotationKind.Crop && layer.IsVisible))
        {
            if (layer.Kind == AnnotationKind.Blur)
            {
                PixelBufferEffects.BoxBlur(canvas, layer.Bounds, 10);
                continue;
            }
            if (layer.Kind == AnnotationKind.Pixelate)
            {
                PixelBufferEffects.Pixelate(canvas, layer.Bounds, 10);
                continue;
            }
            if (layer.Kind == AnnotationKind.Magnify)
            {
                RenderMagnify(canvas, layer);
                continue;
            }
            if (layer.Kind == AnnotationKind.Spotlight)
            {
                RenderSpotlight(canvas, layer);
                continue;
            }

            using var graphics = Graphics.FromImage(canvas);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            RenderVectorLayerWithTransform(graphics, layer);
        }

        var crop = document.Layers.LastOrDefault(layer => layer.Kind == AnnotationKind.Crop && layer.IsVisible);
        if (crop is null) return BitmapCodec.EncodePng(canvas);
        var safe = crop.Bounds.Intersect(new PixelRect(0, 0, canvas.Width, canvas.Height));
        if (safe.IsEmpty) return BitmapCodec.EncodePng(canvas);
        using var cropped = canvas.Clone(new Rectangle(safe.X, safe.Y, safe.Width, safe.Height), PixelFormat.Format32bppArgb);
        return BitmapCodec.EncodePng(cropped);
    }

    private static void RenderVectorLayerWithTransform(Graphics graphics, AnnotationLayer layer)
    {
        var state = graphics.Save();
        try
        {
            if (Math.Abs(layer.RotationDegrees) > .001)
            {
                var center = layer.Bounds.Center;
                graphics.TranslateTransform(center.X, center.Y);
                graphics.RotateTransform((float)layer.RotationDegrees);
                graphics.TranslateTransform(-center.X, -center.Y);
            }
            RenderVectorLayer(graphics, layer);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static void RenderVectorLayer(Graphics graphics, AnnotationLayer layer)
    {
        var color = WithOpacity(FromArgb(layer.Argb), layer.Opacity);
        var rect = new Rectangle(layer.Bounds.X, layer.Bounds.Y, layer.Bounds.Width, layer.Bounds.Height);
        switch (layer.Kind)
        {
            case AnnotationKind.Rectangle:
                FillIfRequested(graphics, layer, rect, ellipse: false);
                using (var pen = CreatePen(color, layer)) graphics.DrawRectangle(pen, rect);
                break;
            case AnnotationKind.Ellipse:
                FillIfRequested(graphics, layer, rect, ellipse: true);
                using (var pen = CreatePen(color, layer)) graphics.DrawEllipse(pen, rect);
                break;
            case AnnotationKind.Line:
                using (var pen = CreatePen(color, layer))
                    graphics.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
                break;
            case AnnotationKind.Arrow:
                using (var pen = CreatePen(color, layer))
                {
                    pen.CustomEndCap = new AdjustableArrowCap(Math.Max(3, layer.StrokeWidth * 2), Math.Max(4, layer.StrokeWidth * 2.5f));
                    graphics.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
                }
                break;
            case AnnotationKind.Freehand:
                DrawFreehand(graphics, layer, color, false);
                break;
            case AnnotationKind.Highlight:
                DrawFreehand(graphics, layer, System.Drawing.Color.FromArgb(Math.Min(90, (int)color.A), color), true);
                break;
            case AnnotationKind.Text:
                DrawText(graphics, layer, color, rect, StringAlignment.Near);
                break;
            case AnnotationKind.SpeechBalloon:
                DrawSpeechBalloon(graphics, layer, color, rect);
                break;
            case AnnotationKind.Callout:
                DrawCallout(graphics, layer, color, rect);
                break;
            case AnnotationKind.StepNumber:
            case AnnotationKind.StepAlpha:
            case AnnotationKind.StepRoman:
                DrawStep(graphics, layer, color, rect);
                break;
            case AnnotationKind.CursorStamp:
                DrawCursorStamp(graphics, layer, color, rect);
                break;
            case AnnotationKind.ClickStamp:
                DrawClickStamp(graphics, layer, color, rect);
                break;
            case AnnotationKind.Emoji:
                DrawEmoji(graphics, layer, color, rect);
                break;
            case AnnotationKind.CurvedLine:
            case AnnotationKind.CurvedArrow:
                using (var pen = CreatePen(color, layer))
                {
                    if (layer.Kind == AnnotationKind.CurvedArrow)
                        pen.CustomEndCap = new AdjustableArrowCap(Math.Max(3, layer.StrokeWidth * 2), Math.Max(4, layer.StrokeWidth * 2.5f));
                    var x1 = rect.Left; var y1 = rect.Bottom; var x2 = rect.Right; var y2 = rect.Top;
                    graphics.DrawBezier(pen, x1, y1, rect.Left + rect.Width * .35f, rect.Top, rect.Left + rect.Width * .65f, rect.Bottom, x2, y2);
                }
                break;
            case AnnotationKind.Bracket:
                using (var pen = CreatePen(color, layer))
                {
                    graphics.DrawLines(pen, [new Point(rect.Right, rect.Top), new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom)]);
                }
                break;
        }
    }


    private static void DrawText(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect, StringAlignment verticalAlignment)
    {
        using var brush = new SolidBrush(color);
        using var font = new Font(
            string.IsNullOrWhiteSpace(layer.FontFamily) ? "Segoe UI" : layer.FontFamily,
            Math.Max(8, layer.FontSize),
            (layer.FontBold ? FontStyle.Bold : FontStyle.Regular) | (layer.FontItalic ? FontStyle.Italic : FontStyle.Regular),
            GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = layer.TextAlignment switch
            {
                AnnotationTextAlignment.Center => StringAlignment.Center,
                AnnotationTextAlignment.Right => StringAlignment.Far,
                _ => StringAlignment.Near
            },
            LineAlignment = verticalAlignment,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(layer.Text ?? string.Empty, font, brush, rect, format);
    }

    private static void DrawSpeechBalloon(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect)
    {
        using var fill = new SolidBrush(WithOpacity(System.Drawing.Color.White, Math.Max(.35f, layer.Opacity)));
        using var pen = CreatePen(color, layer);
        var bubble = new Rectangle(rect.X, rect.Y, rect.Width, Math.Max(1, rect.Height - Math.Max(8, rect.Height / 5)));
        graphics.FillEllipse(fill, bubble); graphics.DrawEllipse(pen, bubble);
        var tailTop = bubble.Bottom - Math.Max(4, bubble.Height / 6);
        var tail = new[] { new Point(bubble.Left + bubble.Width / 4, tailTop), new Point(bubble.Left + bubble.Width / 3, rect.Bottom), new Point(bubble.Left + bubble.Width / 2, bubble.Bottom - 1) };
        graphics.FillPolygon(fill, tail); graphics.DrawLines(pen, tail);
        DrawText(graphics, layer with { TextAlignment = AnnotationTextAlignment.Center }, color, bubble, StringAlignment.Center);
    }

    private static void DrawCallout(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect)
    {
        using var fill = new SolidBrush(WithOpacity(System.Drawing.Color.White, Math.Max(.35f, layer.Opacity)));
        using var pen = CreatePen(color, layer);
        var boxHeight = Math.Max(1, (int)Math.Round(rect.Height * .72));
        var box = new Rectangle(rect.X, rect.Y, rect.Width, boxHeight);
        graphics.FillRectangle(fill, box); graphics.DrawRectangle(pen, box);
        pen.CustomEndCap = new AdjustableArrowCap(Math.Max(3, layer.StrokeWidth * 2), Math.Max(4, layer.StrokeWidth * 2.5f));
        graphics.DrawLine(pen, box.Left + box.Width / 2, box.Bottom, rect.Right, rect.Bottom);
        DrawText(graphics, layer with { TextAlignment = AnnotationTextAlignment.Center }, color, box, StringAlignment.Center);
    }

    private static void DrawStep(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect)
    {
        var diameter = Math.Max(18, Math.Min(rect.Width, rect.Height));
        var circle = new Rectangle(rect.X, rect.Y, diameter, diameter);
        using var fill = new SolidBrush(color);
        using var border = new Pen(System.Drawing.Color.White, Math.Max(1, layer.StrokeWidth / 2));
        graphics.FillEllipse(fill, circle); graphics.DrawEllipse(border, circle);
        using var brush = new SolidBrush(System.Drawing.Color.White);
        using var font = new Font("Segoe UI", Math.Max(10, diameter * .45f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(layer.Text ?? "1", font, brush, circle, format);
    }

    private static void DrawCursorStamp(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect)
    {
        var w = Math.Max(16, rect.Width); var h = Math.Max(20, rect.Height);
        using var path = new GraphicsPath();
        path.AddPolygon([
            new Point(rect.Left, rect.Top),
            new Point(rect.Left, rect.Top + h),
            new Point(rect.Left + w / 3, rect.Top + h * 2 / 3),
            new Point(rect.Left + w / 2, rect.Top + h),
            new Point(rect.Left + w * 2 / 3, rect.Top + h - Math.Max(4, h / 10)),
            new Point(rect.Left + w / 2, rect.Top + h * 3 / 5),
            new Point(rect.Left + w, rect.Top + h * 3 / 5)
        ]);
        using var fill = new SolidBrush(WithOpacity(System.Drawing.Color.White, layer.Opacity));
        using var pen = CreatePen(color, layer);
        graphics.FillPath(fill, path); graphics.DrawPath(pen, path);
    }

    private static void DrawClickStamp(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect)
    {
        var diameter = Math.Max(18, Math.Min(rect.Width, rect.Height));
        var circle = new Rectangle(rect.X, rect.Y, diameter, diameter);
        using var pen = CreatePen(color, layer with { StrokeWidth = Math.Max(2, layer.StrokeWidth) });
        graphics.DrawEllipse(pen, circle);
        var cx = circle.Left + circle.Width / 2; var cy = circle.Top + circle.Height / 2;
        graphics.DrawLine(pen, cx, circle.Top - diameter / 4, cx, circle.Top + diameter / 5);
        graphics.DrawLine(pen, cx, circle.Bottom - diameter / 5, cx, circle.Bottom + diameter / 4);
        graphics.DrawLine(pen, circle.Left - diameter / 4, cy, circle.Left + diameter / 5, cy);
        graphics.DrawLine(pen, circle.Right - diameter / 5, cy, circle.Right + diameter / 4, cy);
    }

    private static void DrawEmoji(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, Rectangle rect)
    {
        using var brush = new SolidBrush(color);
        using var font = new Font("Segoe UI Emoji", Math.Max(16, Math.Min(rect.Width, rect.Height) * .75f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        graphics.DrawString(string.IsNullOrWhiteSpace(layer.Text) ? "🙂" : layer.Text, font, brush, rect, format);
    }

    private static void RenderMagnify(Bitmap canvas, AnnotationLayer layer)
    {
        var safe = layer.Bounds.Intersect(new PixelRect(0, 0, canvas.Width, canvas.Height));
        if (safe.IsEmpty) return;
        var sourceWidth = Math.Max(1, safe.Width / 2); var sourceHeight = Math.Max(1, safe.Height / 2);
        var sourceX = Math.Clamp(safe.Center.X - sourceWidth / 2, 0, Math.Max(0, canvas.Width - sourceWidth));
        var sourceY = Math.Clamp(safe.Center.Y - sourceHeight / 2, 0, Math.Max(0, canvas.Height - sourceHeight));
        using var crop = canvas.Clone(new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(crop, new Rectangle(safe.X, safe.Y, safe.Width, safe.Height));
        using var pen = new Pen(WithOpacity(FromArgb(layer.Argb), layer.Opacity), Math.Max(2, layer.StrokeWidth));
        graphics.DrawEllipse(pen, new Rectangle(safe.X, safe.Y, safe.Width, safe.Height));
    }

    private static void RenderSpotlight(Bitmap canvas, AnnotationLayer layer)
    {
        var safe = layer.Bounds.Intersect(new PixelRect(0, 0, canvas.Width, canvas.Height));
        if (safe.IsEmpty) return;
        using var graphics = Graphics.FromImage(canvas);
        using var region = new Region(new Rectangle(0, 0, canvas.Width, canvas.Height));
        using var hole = new GraphicsPath(); hole.AddEllipse(new Rectangle(safe.X, safe.Y, safe.Width, safe.Height));
        region.Exclude(hole);
        using var shade = new SolidBrush(System.Drawing.Color.FromArgb((int)Math.Round(170 * Math.Clamp(layer.Opacity, 0f, 1f)), 0, 0, 0));
        graphics.FillRegion(shade, region);
        using var pen = new Pen(WithOpacity(FromArgb(layer.Argb), layer.Opacity), Math.Max(2, layer.StrokeWidth));
        graphics.DrawEllipse(pen, new Rectangle(safe.X, safe.Y, safe.Width, safe.Height));
    }

    private static void FillIfRequested(Graphics graphics, AnnotationLayer layer, Rectangle rect, bool ellipse)
    {
        if (layer.FillArgb is not { } fillArgb) return;
        using var brush = new SolidBrush(WithOpacity(FromArgb(fillArgb), layer.Opacity));
        if (ellipse) graphics.FillEllipse(brush, rect);
        else graphics.FillRectangle(brush, rect);
    }

    private static Pen CreatePen(System.Drawing.Color color, AnnotationLayer layer)
    {
        return new Pen(color, Math.Max(1, layer.StrokeWidth))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            DashStyle = layer.LineStyle switch
            {
                AnnotationLineStyle.Dash => DashStyle.Dash,
                AnnotationLineStyle.Dot => DashStyle.Dot,
                _ => DashStyle.Solid
            }
        };
    }

    private static void DrawFreehand(Graphics graphics, AnnotationLayer layer, System.Drawing.Color color, bool highlight)
    {
        if (layer.Points is null || layer.Points.Count < 2) return;
        using var pen = CreatePen(color, layer with { StrokeWidth = highlight ? Math.Max(8, layer.StrokeWidth * 4) : layer.StrokeWidth });
        graphics.DrawLines(pen, layer.Points.Select(point => new Point(point.X, point.Y)).ToArray());
    }

    private static System.Drawing.Color WithOpacity(System.Drawing.Color color, float opacity)
    {
        var factor = Math.Clamp(opacity, 0f, 1f);
        return System.Drawing.Color.FromArgb((int)Math.Round(color.A * factor), color.R, color.G, color.B);
    }

    private static System.Drawing.Color FromArgb(uint value) => System.Drawing.Color.FromArgb(
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
