using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Utilities;

internal enum ImageBorderPreset
{
    Simple,
    Double,
    Photo,
    Dark,
}

internal sealed class ImageCanvasOperationsService
{
    public byte[] AddBorder(byte[] imageBytes, int thickness, uint argb)
    {
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        thickness = Math.Clamp(thickness, 1, 256);
        var width = checked(source.Width + thickness * 2);
        var height = checked(source.Height + thickness * 2);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        using var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.FromArgb(unchecked((int)argb)));
        graphics.DrawImageUnscaled(source, thickness, thickness);
        return BitmapCodec.EncodePng(output);
    }

    public byte[] AddBorderPreset(byte[] imageBytes, ImageBorderPreset preset)
    {
        return preset switch
        {
            ImageBorderPreset.Simple => AddBorder(imageBytes, 8, 0xFFFFFFFF),
            ImageBorderPreset.Double => AddBorder(AddBorder(imageBytes, 4, 0xFF202020), 8, 0xFFF4F4F4),
            ImageBorderPreset.Photo => AddBorder(AddBorder(imageBytes, 2, 0xFFB8B8B8), 18, 0xFFFFFFFF),
            ImageBorderPreset.Dark => AddBorder(imageBytes, 12, 0xFF202124),
            _ => AddBorder(imageBytes, 8, 0xFFFFFFFF),
        };
    }

    public byte[] TornEdges(byte[] imageBytes, int depth)
    {
        using var bitmap = CloneArgb(imageBytes);
        depth = Math.Clamp(depth, 2, Math.Max(2, Math.Min(bitmap.Width, bitmap.Height) / 4));
        var pixels = BitmapPixelBuffer.ReadBgra(bitmap);
        var width = bitmap.Width; var height = bitmap.Height;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var nearest = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
            var jitter = 0.35 + Hash01(x * 73856093 ^ y * 19349663) * 0.65;
            var cut = depth * jitter;
            if (nearest >= cut) continue;
            var i = (y * width + x) * 4;
            pixels[i + 3] = nearest < cut * 0.72 ? (byte)0 : (byte)Math.Clamp((int)Math.Round(255 * (nearest - cut * 0.72) / Math.Max(1, cut * 0.28)), 0, 255);
        }
        BitmapPixelBuffer.WriteBgra(bitmap, pixels);
        return BitmapCodec.EncodePng(bitmap);
    }

    public byte[] FadeEdges(byte[] imageBytes, int depth)
    {
        using var bitmap = CloneArgb(imageBytes);
        depth = Math.Clamp(depth, 1, Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 2));
        var pixels = BitmapPixelBuffer.ReadBgra(bitmap);
        var width = bitmap.Width; var height = bitmap.Height;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var nearest = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
            if (nearest >= depth) continue;
            var i = (y * width + x) * 4;
            var factor = Math.Clamp(nearest / (double)depth, 0, 1);
            pixels[i + 3] = (byte)Math.Round(pixels[i + 3] * factor);
        }
        BitmapPixelBuffer.WriteBgra(bitmap, pixels);
        return BitmapCodec.EncodePng(bitmap);
    }

    public byte[] AddReflection(byte[] imageBytes, int reflectionPercent = 30, int opacityPercent = 45)
    {
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        reflectionPercent = Math.Clamp(reflectionPercent, 5, 75);
        opacityPercent = Math.Clamp(opacityPercent, 1, 100);
        var reflectionHeight = Math.Max(1, checked(source.Height * reflectionPercent / 100));
        var outputHeight = checked(source.Height + reflectionHeight);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(source.Width, outputHeight);
        var sourcePixels = BitmapPixelBuffer.ReadBgra(source);
        using var output = new Bitmap(source.Width, outputHeight, PixelFormat.Format32bppArgb);
        var outputPixels = new byte[checked(source.Width * outputHeight * 4)];
        var sourceRowBytes = source.Width * 4;
        Buffer.BlockCopy(sourcePixels, 0, outputPixels, 0, sourcePixels.Length);
        for (var y = 0; y < reflectionHeight; y++)
        {
            var sampleY = source.Height - 1 - Math.Min(source.Height - 1, (int)Math.Floor(y * source.Height / (double)reflectionHeight));
            var fade = (1d - y / (double)Math.Max(1, reflectionHeight - 1)) * opacityPercent / 100d;
            var sourceOffset = sampleY * sourceRowBytes;
            var targetOffset = (source.Height + y) * sourceRowBytes;
            for (var x = 0; x < source.Width; x++)
            {
                var si = sourceOffset + x * 4;
                var ti = targetOffset + x * 4;
                outputPixels[ti] = sourcePixels[si];
                outputPixels[ti + 1] = sourcePixels[si + 1];
                outputPixels[ti + 2] = sourcePixels[si + 2];
                outputPixels[ti + 3] = (byte)Math.Round(sourcePixels[si + 3] * fade);
            }
        }
        BitmapPixelBuffer.WriteBgra(output, outputPixels);
        return BitmapCodec.EncodePng(output);
    }

    public byte[] AddTextWatermark(byte[] imageBytes, string text, int fontSize = 24, int opacityPercent = 65)
    {
        text = NormalizeText(text);
        using var bitmap = CloneArgb(imageBytes);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        fontSize = Math.Clamp(fontSize, 8, 160);
        opacityPercent = Math.Clamp(opacityPercent, 1, 100);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var size = graphics.MeasureString(text, font);
        var padding = Math.Max(6, fontSize / 3);
        var x = Math.Max(0, bitmap.Width - size.Width - padding);
        var y = Math.Max(0, bitmap.Height - size.Height - padding);
        using var shadow = new SolidBrush(Color.FromArgb((int)(180 * opacityPercent / 100d), Color.Black));
        using var brush = new SolidBrush(Color.FromArgb((int)(255 * opacityPercent / 100d), Color.White));
        graphics.DrawString(text, font, shadow, x + 1, y + 1);
        graphics.DrawString(text, font, brush, x, y);
        return BitmapCodec.EncodePng(bitmap);
    }

    public byte[] AddImageWatermark(byte[] imageBytes, byte[] watermarkBytes, int scalePercent = 20, int opacityPercent = 70)
    {
        ArgumentNullException.ThrowIfNull(watermarkBytes);
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        using var watermark = BitmapCodec.DecodeForPixelProcessing(watermarkBytes);
        scalePercent = Math.Clamp(scalePercent, 5, 80);
        opacityPercent = Math.Clamp(opacityPercent, 1, 100);
        var targetWidth = Math.Max(1, source.Width * scalePercent / 100);
        var ratio = targetWidth / (double)watermark.Width;
        var targetHeight = Math.Max(1, (int)Math.Round(watermark.Height * ratio));
        if (targetHeight > source.Height * 4 / 5)
        {
            targetHeight = Math.Max(1, source.Height * 4 / 5);
            targetWidth = Math.Max(1, (int)Math.Round(watermark.Width * (targetHeight / (double)watermark.Height)));
        }
        using var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.DrawImageUnscaled(source, 0, 0);
        using var attributes = new ImageAttributes();
        var alpha = opacityPercent / 100f;
        attributes.SetColorMatrix(new ColorMatrix { Matrix00 = 1, Matrix11 = 1, Matrix22 = 1, Matrix33 = alpha, Matrix44 = 1 });
        var margin = Math.Max(8, Math.Min(source.Width, source.Height) / 100);
        var destination = new Rectangle(Math.Max(0, source.Width - targetWidth - margin), Math.Max(0, source.Height - targetHeight - margin), targetWidth, targetHeight);
        graphics.DrawImage(watermark, destination, 0, 0, watermark.Width, watermark.Height, GraphicsUnit.Pixel, attributes);
        return BitmapCodec.EncodePng(output);
    }

    public byte[] AddDateTimeStamp(byte[] imageBytes, DateTimeOffset timestamp) =>
        AddTextWatermark(imageBytes, timestamp.ToString("yyyy-MM-dd HH:mm:ss"), 22, 70);

    public byte[] AddCaptureInformationStamp(byte[] imageBytes, string information) =>
        AddTextWatermark(imageBytes, NormalizeText(information), 20, 70);

    public byte[] AutoCropPlainBorders(byte[] imageBytes, int tolerance = 8)
    {
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        var pixels = BitmapPixelBuffer.ReadBgra(source);
        tolerance = Math.Clamp(tolerance, 0, 64);
        var background = AverageCorners(pixels, source.Width, source.Height);
        var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var i = (y * source.Width + x) * 4;
            if (IsNear(pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3], background, tolerance)) continue;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top || (left == 0 && top == 0 && right == source.Width - 1 && bottom == source.Height - 1)) return imageBytes.ToArray();
        var rect = new Rectangle(left, top, right - left + 1, bottom - top + 1);
        using var cropped = source.Clone(rect, PixelFormat.Format32bppArgb);
        return BitmapCodec.EncodePng(cropped);
    }

    public byte[] ExpandCanvas(byte[] imageBytes, int padding, uint argb = 0x00000000)
    {
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        padding = Math.Clamp(padding, 1, 4096);
        var width = checked(source.Width + padding * 2);
        var height = checked(source.Height + padding * 2);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        using var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.FromArgb(unchecked((int)argb)));
        graphics.DrawImageUnscaled(source, padding, padding);
        return BitmapCodec.EncodePng(output);
    }

    public byte[] MakeColorTransparent(byte[] imageBytes, uint argb, int tolerance)
    {
        using var bitmap = CloneArgb(imageBytes);
        tolerance = Math.Clamp(tolerance, 0, 255);
        var target = Color.FromArgb(unchecked((int)argb));
        var pixels = BitmapPixelBuffer.ReadBgra(bitmap);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (Math.Abs(pixels[i + 2] - target.R) <= tolerance && Math.Abs(pixels[i + 1] - target.G) <= tolerance && Math.Abs(pixels[i] - target.B) <= tolerance)
                pixels[i + 3] = 0;
        }
        BitmapPixelBuffer.WriteBgra(bitmap, pixels);
        return BitmapCodec.EncodePng(bitmap);
    }

    public byte[] RotateArbitrary(byte[] imageBytes, double degrees, uint backgroundArgb = 0x00000000)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Abs(Math.Cos(radians)); var sin = Math.Abs(Math.Sin(radians));
        var width = Math.Max(1, (int)Math.Ceiling(source.Width * cos + source.Height * sin));
        var height = Math.Max(1, (int)Math.Ceiling(source.Width * sin + source.Height * cos));
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        using var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.FromArgb(unchecked((int)backgroundArgb)));
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TranslateTransform(width / 2f, height / 2f);
        graphics.RotateTransform((float)degrees);
        graphics.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
        graphics.DrawImageUnscaled(source, 0, 0);
        return BitmapCodec.EncodePng(output);
    }

    private static Bitmap CloneArgb(byte[] imageBytes)
    {
        using var source = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.DrawImageUnscaled(source, 0, 0);
        return output;
    }

    private static string NormalizeText(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("Watermark text is required.", nameof(text));
        if (value.Length > 512) value = value[..512];
        return value.Replace('\0', ' ');
    }

    private static (byte R, byte G, byte B, byte A) AverageCorners(byte[] pixels, int width, int height)
    {
        var points = new[] { 0, (width - 1) * 4, ((height - 1) * width) * 4, (height * width - 1) * 4 };
        var r = 0; var g = 0; var b = 0; var a = 0;
        foreach (var i in points) { b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; a += pixels[i + 3]; }
        return ((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
    }

    private static bool IsNear(byte r, byte g, byte b, byte a, (byte R, byte G, byte B, byte A) target, int tolerance) =>
        Math.Abs(r - target.R) <= tolerance && Math.Abs(g - target.G) <= tolerance && Math.Abs(b - target.B) <= tolerance && Math.Abs(a - target.A) <= tolerance;

    private static double Hash01(int value)
    {
        unchecked
        {
            uint x = (uint)value;
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x / (double)uint.MaxValue;
        }
    }
}
