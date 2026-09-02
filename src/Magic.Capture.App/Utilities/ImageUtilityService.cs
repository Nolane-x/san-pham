using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Utilities;

namespace Magic.Capture.App.Utilities;

internal enum ThumbnailMode { Fit, Fill }

internal sealed class ImageUtilityService
{
    public byte[] Combine(IReadOnlyList<byte[]> images, ImageCombineMode mode, int spacing = 0, int gridColumns = 2)
    {
        if (images.Count == 0) throw new ArgumentException("At least one image is required.", nameof(images));
        if (images.Count > 128) throw new ArgumentException("Combine supports at most 128 images per operation.", nameof(images));

        var sizes = new (int Width, int Height)[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            ImageWorkloadLimits.ValidateEncodedLength(images[i].LongLength);
            if (!PngDimensions.TryRead(images[i], out var width, out var height))
                throw new InvalidDataException($"Combine input {i + 1} is not a valid PNG image.");
            ImageWorkloadLimits.ValidateDimensions(width, height);
            sizes[i] = (width, height);
        }

        var placements = ImageCombineLayout.Create(sizes, mode, spacing, gridColumns);
        var outputWidth = placements.Max(r => r.Right);
        var outputHeight = placements.Max(r => r.Bottom);
        ImageWorkloadLimits.ValidateDimensions(outputWidth, outputHeight);

        using var canvas = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        for (var i = 0; i < images.Count; i++)
        {
            using var bitmap = BitmapCodec.Decode(images[i]);
            var r = placements[i];
            g.DrawImage(bitmap, new Rectangle(r.X, r.Y, r.Width, r.Height));
        }
        return BitmapCodec.EncodePng(canvas);
    }

    public IReadOnlyList<byte[]> Split(byte[] imageBytes, int rows, int columns)
    {
        using var source = BitmapCodec.Decode(imageBytes);
        var rects = ImageSplitPlan.Create(source.Width, source.Height, rows, columns);
        var result = new List<byte[]>(rects.Count);
        foreach (var r in rects)
        {
            using var part = source.Clone(new Rectangle(r.X, r.Y, r.Width, r.Height), PixelFormat.Format32bppArgb);
            result.Add(BitmapCodec.EncodePng(part));
        }
        return result;
    }

    public byte[] Thumbnail(byte[] imageBytes, int width, int height, ThumbnailMode mode = ThumbnailMode.Fit)
    {
        ImageWorkloadLimits.ValidateDimensions(width, height);
        using var source = BitmapCodec.Decode(imageBytes);
        var scale = mode == ThumbnailMode.Fit
            ? Math.Min(width / (double)source.Width, height / (double)source.Height)
            : Math.Max(width / (double)source.Width, height / (double)source.Height);
        var drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(target);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var x = (width - drawWidth) / 2;
        var y = (height - drawHeight) / 2;
        g.DrawImage(source, new Rectangle(x, y, drawWidth, drawHeight));
        return BitmapCodec.EncodePng(target);
    }

    public byte[] StripMetadata(byte[] imageBytes)
    {
        using var bitmap = BitmapCodec.Decode(imageBytes);
        return BitmapCodec.EncodePng(bitmap);
    }

    public byte[] Beautify(byte[] imageBytes, BeautifyOptions options)
    {
        options = options.Normalize();
        using var source = BitmapCodec.Decode(imageBytes);
        var shadowExtent = options.ShadowBlur > 0 && options.ShadowOpacity > 0 ? options.ShadowBlur + 12 : 0;
        var x = options.Padding + shadowExtent;
        var y = options.Padding + shadowExtent;
        var width = checked(source.Width + (options.Padding + shadowExtent) * 2);
        var height = checked(source.Height + (options.Padding + shadowExtent) * 2);
        ImageWorkloadLimits.ValidateDimensions(width, height);
        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(ParseColor(options.Background, Color.FromArgb(243, 243, 243)));

        var targetRect = new Rectangle(x, y, source.Width, source.Height);
        if (shadowExtent > 0)
            DrawSoftShadow(g, targetRect, options);

        using var clipPath = RoundedRect(targetRect, Math.Min(options.CornerRadius, Math.Min(targetRect.Width, targetRect.Height) / 2));
        using var previousClip = g.Clip;
        g.SetClip(clipPath);
        g.DrawImage(source, targetRect);
        g.Clip = previousClip;

        if (options.BorderWidth > 0)
        {
            using var pen = new Pen(ParseColor(options.BorderColor, Color.Transparent), options.BorderWidth);
            using var borderPath = RoundedRect(targetRect, Math.Min(options.CornerRadius, Math.Min(targetRect.Width, targetRect.Height) / 2));
            g.DrawPath(pen, borderPath);
        }

        return BitmapCodec.EncodePng(canvas);
    }

    private static void DrawSoftShadow(Graphics g, Rectangle rect, BeautifyOptions options)
    {
        var color = ParseColor(options.ShadowColor, Color.Black);
        var layers = Math.Clamp(options.ShadowBlur / 3, 3, 48);
        for (var i = layers; i >= 1; i--)
        {
            var alpha = (int)Math.Round(255 * options.ShadowOpacity * (1d - i / (double)(layers + 1)) / layers * 2.4);
            alpha = Math.Clamp(alpha, 1, 96);
            using var brush = new SolidBrush(Color.FromArgb(alpha, color));
            var expand = i * 2;
            var shadowRect = new Rectangle(rect.X - expand / 2 + 6, rect.Y - expand / 2 + 8, rect.Width + expand, rect.Height + expand);
            using var path = RoundedRect(shadowRect, Math.Min(options.CornerRadius + expand / 2, Math.Min(shadowRect.Width, shadowRect.Height) / 2));
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }
        var diameter = radius * 2;
        var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        var hex = (value ?? string.Empty).Trim().TrimStart('#');
        static bool ParseByte(ReadOnlySpan<char> text, out int value) =>
            int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        if (hex.Length == 6
            && ParseByte(hex.AsSpan(0, 2), out var r)
            && ParseByte(hex.AsSpan(2, 2), out var g)
            && ParseByte(hex.AsSpan(4, 2), out var b))
            return Color.FromArgb(255, r, g, b);

        if (hex.Length == 8
            && ParseByte(hex.AsSpan(0, 2), out var a)
            && ParseByte(hex.AsSpan(2, 2), out r)
            && ParseByte(hex.AsSpan(4, 2), out g)
            && ParseByte(hex.AsSpan(6, 2), out b))
            return Color.FromArgb(a, r, g, b);

        return fallback;
    }
}
