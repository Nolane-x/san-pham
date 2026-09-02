using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed record RenderedCaptureRegion(PixelRect Bounds, byte[] PngBytes);

internal static class CaptureSelectionImageRenderer
{
    public static byte[] Render(byte[] frozenPng, CaptureSelectionGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(frozenPng);
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Bounds.IsEmpty) throw new InvalidDataException("Capture selection bounds are empty.");

        if (geometry.Kind == CaptureSelectionKind.Rectangle)
            return BitmapCodec.CropPng(frozenPng, geometry.Bounds);

        ImageWorkloadLimits.ValidateEncodedLength(frozenPng.LongLength);
        using var stream = new MemoryStream(frozenPng, writable: false);
        using var source = new Bitmap(stream);
        ImageWorkloadLimits.ValidateDimensions(source.Width, source.Height);
        var sourceBounds = new PixelRect(0, 0, source.Width, source.Height);
        var bounds = geometry.Bounds.Intersect(sourceBounds);
        if (bounds != geometry.Bounds || bounds.IsEmpty)
            throw new InvalidDataException("Capture selection extends outside the frozen source image.");
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(bounds.Width, bounds.Height);

        return geometry.Kind switch
        {
            CaptureSelectionKind.Ellipse => RenderMasked(source, geometry, bounds, CreateEllipsePath),
            CaptureSelectionKind.Polygon => RenderMasked(source, geometry, bounds, CreatePointPath),
            CaptureSelectionKind.Freehand => RenderMasked(source, geometry, bounds, CreatePointPath),
            CaptureSelectionKind.MultiRegion => RenderMultiRegion(source, geometry, bounds),
            _ => throw new InvalidDataException($"Unsupported capture selection kind: {geometry.Kind}.")
        };
    }

    public static IReadOnlyList<RenderedCaptureRegion> RenderSeparateRegions(byte[] frozenPng, CaptureSelectionGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(frozenPng);
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Kind != CaptureSelectionKind.MultiRegion)
            throw new InvalidDataException("Separate-region rendering requires multi-region geometry.");
        if (geometry.Regions.Count is < 1 or > CaptureSelectionGeometryRules.MaximumRegions)
            throw new InvalidDataException("Multi-region capture has an invalid region count.");

        CaptureSelectionOutputPolicy.ValidateSeparateRegions(geometry.Regions);

        ImageWorkloadLimits.ValidateEncodedLength(frozenPng.LongLength);
        using var stream = new MemoryStream(frozenPng, writable: false);
        using var source = new Bitmap(stream);
        ImageWorkloadLimits.ValidateDimensions(source.Width, source.Height);
        var sourceBounds = new PixelRect(0, 0, source.Width, source.Height);
        var rendered = new List<RenderedCaptureRegion>(geometry.Regions.Count);
        long totalEncodedBytes = 0;

        foreach (var region in geometry.Regions)
        {
            var safe = region.Intersect(sourceBounds);
            if (safe != region || safe.IsEmpty)
                throw new InvalidDataException("Multi-region capture contains an out-of-range region.");
            ImageWorkloadLimits.ValidatePixelProcessingDimensions(region.Width, region.Height);
            using var canvas = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, region.Width, region.Height),
                    new Rectangle(region.X, region.Y, region.Width, region.Height),
                    GraphicsUnit.Pixel);
            }
            var png = BitmapCodec.EncodePng(canvas);
            totalEncodedBytes = checked(totalEncodedBytes + png.LongLength);
            ImageWorkloadLimits.ValidateResidentSelectionBytes(totalEncodedBytes);
            rendered.Add(new RenderedCaptureRegion(region, png));
        }

        return rendered;
    }

    private static byte[] RenderMasked(
        Bitmap source,
        CaptureSelectionGeometry geometry,
        PixelRect bounds,
        Func<CaptureSelectionGeometry, PixelRect, GraphicsPath> pathFactory)
    {
        using var canvas = CreateTransparentCanvas(bounds.Width, bounds.Height);
        using var path = pathFactory(geometry, bounds);
        if (path.PointCount < 3) throw new InvalidDataException("Capture mask has insufficient geometry.");

        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.SetClip(path, CombineMode.Replace);
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, bounds.Width, bounds.Height),
                new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                GraphicsUnit.Pixel);
        }
        return BitmapCodec.EncodePng(canvas);
    }

    private static byte[] RenderMultiRegion(Bitmap source, CaptureSelectionGeometry geometry, PixelRect bounds)
    {
        if (geometry.Regions.Count is < 1 or > CaptureSelectionGeometryRules.MaximumRegions)
            throw new InvalidDataException("Multi-region capture has an invalid region count.");

        using var canvas = CreateTransparentCanvas(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(canvas);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        foreach (var region in geometry.Regions)
        {
            var safe = region.Intersect(new PixelRect(0, 0, source.Width, source.Height));
            if (safe != region || safe.IsEmpty) throw new InvalidDataException("Multi-region capture contains an out-of-range region.");
            var destination = new Rectangle(region.X - bounds.X, region.Y - bounds.Y, region.Width, region.Height);
            graphics.DrawImage(source, destination, new Rectangle(region.X, region.Y, region.Width, region.Height), GraphicsUnit.Pixel);
        }
        return BitmapCodec.EncodePng(canvas);
    }

    private static Bitmap CreateTransparentCanvas(int width, int height)
    {
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        return canvas;
    }

    private static GraphicsPath CreateEllipsePath(CaptureSelectionGeometry geometry, PixelRect bounds)
    {
        var path = new GraphicsPath(FillMode.Winding);
        path.AddEllipse(0, 0, bounds.Width, bounds.Height);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreatePointPath(CaptureSelectionGeometry geometry, PixelRect bounds)
    {
        if (geometry.Points.Count < 3 || geometry.Points.Count > CaptureSelectionGeometryRules.MaximumPathPoints)
            throw new InvalidDataException("Path capture has an invalid point count.");
        var points = geometry.Points.Select(point => new PointF(point.X - bounds.X, point.Y - bounds.Y)).ToArray();
        var path = new GraphicsPath(FillMode.Winding);
        path.AddPolygon(points);
        return path;
    }
}
