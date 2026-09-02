using System.Drawing;
using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Utilities;

internal sealed class ImageEffectPipelineService
{
    public byte[] Apply(byte[] imageBytes, ImageEffectPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        pipeline = (pipeline ?? throw new ArgumentNullException(nameof(pipeline))).Normalize();
        if (pipeline.Steps.Count == 0) return imageBytes.ToArray();
        using var decoded = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        using var argb = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(argb)) graphics.DrawImageUnscaled(decoded, 0, 0);
        var pixels = BitmapPixelBuffer.ReadBgra(argb);
        byte[]? scratch = null;
        foreach (var step in pipeline.Steps)
        {
            var normalized = step.Normalize();
            if (IsNeighborhoodEffect(normalized.Kind))
            {
                scratch ??= new byte[pixels.Length];
                ApplyNeighborhoodEffect(pixels, scratch, argb.Width, argb.Height, normalized);
            }
            else
            {
                ApplyPixelEffect(pixels, normalized);
            }
        }
        BitmapPixelBuffer.WriteBgra(argb, pixels);
        return BitmapCodec.EncodePng(argb);
    }

    private static bool IsNeighborhoodEffect(ImageEffectKind kind) => kind is
        ImageEffectKind.Sharpen or ImageEffectKind.NoiseReduction or ImageEffectKind.EdgeDetection or ImageEffectKind.Mosaic;

    private static void ApplyPixelEffect(byte[] pixels, ImageEffectStep step)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            (r, g, b) = Transform(r, g, b, step);
            pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r;
        }
    }

    private static (byte R, byte G, byte B) Transform(byte r, byte g, byte b, ImageEffectStep step)
    {
        double rd = r, gd = g, bd = b;
        switch (step.Kind)
        {
            case ImageEffectKind.Brightness:
                var add = step.Amount * 2.55; rd += add; gd += add; bd += add; break;
            case ImageEffectKind.Contrast:
                var c = step.Amount * 2.55;
                var factor = (259 * (c + 255)) / (255 * (259 - c));
                rd = factor * (rd - 128) + 128; gd = factor * (gd - 128) + 128; bd = factor * (bd - 128) + 128; break;
            case ImageEffectKind.Gamma:
                rd = 255 * Math.Pow(rd / 255, 1 / step.Amount); gd = 255 * Math.Pow(gd / 255, 1 / step.Amount); bd = 255 * Math.Pow(bd / 255, 1 / step.Amount); break;
            case ImageEffectKind.Exposure:
                var exposure = Math.Pow(2, step.Amount); rd *= exposure; gd *= exposure; bd *= exposure; break;
            case ImageEffectKind.Hue:
                return ApplyHue(r, g, b, step.Amount);
            case ImageEffectKind.Saturation:
                var gray = 0.2126 * rd + 0.7152 * gd + 0.0722 * bd; var sat = 1 + step.Amount / 100;
                rd = gray + (rd - gray) * sat; gd = gray + (gd - gray) * sat; bd = gray + (bd - gray) * sat; break;
            case ImageEffectKind.Vibrance:
                return ApplyVibrance(r, g, b, step.Amount);
            case ImageEffectKind.ColorBalance:
                return ApplyColorBalance(r, g, b, step.Amount, step.SecondaryAmount, step.TertiaryAmount);
            case ImageEffectKind.Grayscale:
                var lum = 0.2126 * rd + 0.7152 * gd + 0.0722 * bd; rd = gd = bd = lum; break;
            case ImageEffectKind.Sepia:
                var sr = 0.393 * rd + 0.769 * gd + 0.189 * bd;
                var sg = 0.349 * rd + 0.686 * gd + 0.168 * bd;
                var sb = 0.272 * rd + 0.534 * gd + 0.131 * bd; rd = sr; gd = sg; bd = sb; break;
            case ImageEffectKind.Invert:
                rd = 255 - rd; gd = 255 - gd; bd = 255 - bd; break;
            case ImageEffectKind.Posterize:
                var levels = Math.Clamp((int)Math.Round(step.Amount), 2, 32); rd = Quantize(rd, levels); gd = Quantize(gd, levels); bd = Quantize(bd, levels); break;
            case ImageEffectKind.Threshold:
                var y = 0.2126 * rd + 0.7152 * gd + 0.0722 * bd; rd = gd = bd = y >= step.Amount ? 255 : 0; break;
        }
        return (Clamp(rd), Clamp(gd), Clamp(bd));
    }

    private static (byte R, byte G, byte B) ApplyHue(byte r, byte g, byte b, double degrees)
    {
        RgbToHsl(r / 255d, g / 255d, b / 255d, out var h, out var s, out var l);
        h = (h + degrees / 360d) % 1d;
        if (h < 0) h += 1d;
        HslToRgb(h, s, l, out var rd, out var gd, out var bd);
        return (Clamp(rd * 255), Clamp(gd * 255), Clamp(bd * 255));
    }

    private static (byte R, byte G, byte B) ApplyVibrance(byte r, byte g, byte b, double amount)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var average = (r + g + b) / 3d;
        var chroma = max - average;
        var strength = amount / 100d;
        var adaptive = strength * (1d - chroma / 255d);
        return (
            Clamp(r + (r - average) * adaptive),
            Clamp(g + (g - average) * adaptive),
            Clamp(b + (b - average) * adaptive));
    }

    private static (byte R, byte G, byte B) ApplyColorBalance(byte r, byte g, byte b, double red, double green, double blue) =>
        (Clamp(r + red * 2.55), Clamp(g + green * 2.55), Clamp(b + blue * 2.55));

    private static void ApplyNeighborhoodEffect(byte[] pixels, byte[] scratch, int width, int height, ImageEffectStep step)
    {
        switch (step.Kind)
        {
            case ImageEffectKind.Sharpen:
                ApplySharpen(pixels, scratch, width, height, step.Amount);
                break;
            case ImageEffectKind.NoiseReduction:
                ApplyBoxBlur(pixels, scratch, width, height, (int)Math.Round(step.Amount));
                break;
            case ImageEffectKind.EdgeDetection:
                ApplyEdgeDetection(pixels, scratch, width, height, step.Amount);
                break;
            case ImageEffectKind.Mosaic:
                ApplyMosaic(pixels, width, height, (int)Math.Round(step.Amount));
                break;
        }
    }

    private static void ApplySharpen(byte[] source, byte[] scratch, int width, int height, double amount)
    {
        Buffer.BlockCopy(source, 0, scratch, 0, source.Length);
        var strength = Math.Clamp(amount, 0, 5);
        if (strength <= 0) return;
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            var i = (y * width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
            {
                var center = source[i + channel];
                var neighbor = (source[i - 4 + channel] + source[i + 4 + channel] + source[i - width * 4 + channel] + source[i + width * 4 + channel]) / 4d;
                scratch[i + channel] = Clamp(center + (center - neighbor) * strength);
            }
            scratch[i + 3] = source[i + 3];
        }
        Buffer.BlockCopy(scratch, 0, source, 0, source.Length);
    }

    private static void ApplyBoxBlur(byte[] source, byte[] scratch, int width, int height, int radius)
    {
        radius = Math.Clamp(radius, 1, 4);
        Buffer.BlockCopy(source, 0, scratch, 0, source.Length);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            long b = 0, g = 0, r = 0; var count = 0;
            for (var yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++)
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
            {
                var j = (yy * width + xx) * 4;
                b += source[j]; g += source[j + 1]; r += source[j + 2]; count++;
            }
            var i = (y * width + x) * 4;
            scratch[i] = (byte)(b / count); scratch[i + 1] = (byte)(g / count); scratch[i + 2] = (byte)(r / count); scratch[i + 3] = source[i + 3];
        }
        Buffer.BlockCopy(scratch, 0, source, 0, source.Length);
    }

    private static void ApplyEdgeDetection(byte[] source, byte[] scratch, int width, int height, double strength)
    {
        Array.Clear(scratch);
        var gain = Math.Clamp(strength, 0.1, 5);
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            double gx = 0, gy = 0;
            for (var ky = -1; ky <= 1; ky++)
            for (var kx = -1; kx <= 1; kx++)
            {
                var i = ((y + ky) * width + (x + kx)) * 4;
                var lum = 0.0722 * source[i] + 0.7152 * source[i + 1] + 0.2126 * source[i + 2];
                var sx = kx switch { -1 => ky == 0 ? -2 : -1, 0 => 0, _ => ky == 0 ? 2 : 1 };
                var sy = ky switch { -1 => kx == 0 ? -2 : -1, 0 => 0, _ => kx == 0 ? 2 : 1 };
                gx += lum * sx; gy += lum * sy;
            }
            var edge = Clamp(Math.Sqrt(gx * gx + gy * gy) * gain);
            var o = (y * width + x) * 4;
            scratch[o] = scratch[o + 1] = scratch[o + 2] = edge; scratch[o + 3] = source[o + 3];
        }
        for (var x = 0; x < width; x++)
        {
            CopyAlpha(source, scratch, x * 4);
            CopyAlpha(source, scratch, ((height - 1) * width + x) * 4);
        }
        for (var y = 0; y < height; y++)
        {
            CopyAlpha(source, scratch, (y * width) * 4);
            CopyAlpha(source, scratch, (y * width + width - 1) * 4);
        }
        Buffer.BlockCopy(scratch, 0, source, 0, source.Length);
    }

    private static void ApplyMosaic(byte[] pixels, int width, int height, int blockSize)
    {
        blockSize = Math.Clamp(blockSize, 2, 64);
        for (var top = 0; top < height; top += blockSize)
        for (var left = 0; left < width; left += blockSize)
        {
            long b = 0, g = 0, r = 0, a = 0; var count = 0;
            var bottom = Math.Min(height, top + blockSize); var right = Math.Min(width, left + blockSize);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var i = (y * width + x) * 4;
                b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; a += pixels[i + 3]; count++;
            }
            var ab = (byte)(b / count); var ag = (byte)(g / count); var ar = (byte)(r / count); var aa = (byte)(a / count);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = ab; pixels[i + 1] = ag; pixels[i + 2] = ar; pixels[i + 3] = aa;
            }
        }
    }

    private static void CopyAlpha(byte[] source, byte[] target, int index) => target[index + 3] = source[index + 3];

    private static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
    {
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2d;
        if (Math.Abs(max - min) < 1e-12) { h = s = 0; return; }
        var d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h /= 6d;
    }

    private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
    {
        if (s <= 0) { r = g = b = l; return; }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q;
        r = HueToRgb(p, q, h + 1d / 3d); g = HueToRgb(p, q, h); b = HueToRgb(p, q, h - 1d / 3d);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1d / 6d) return p + (q - p) * 6 * t;
        if (t < 1d / 2d) return q;
        if (t < 2d / 3d) return p + (q - p) * (2d / 3d - t) * 6;
        return p;
    }

    private static double Quantize(double value, int levels)
    {
        var step = 255d / (levels - 1);
        return Math.Round(value / step) * step;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
