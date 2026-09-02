using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Magic.Capture.Core.Color;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed class ImageTransformService
{
    public byte[] Crop(byte[] pngBytes, PixelRect bounds) => BitmapCodec.CropPng(pngBytes, bounds);

    public byte[] Resize(byte[] imageBytes, int width, int height)
    {
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        using var source = BitmapCodec.Decode(imageBytes);
        using var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return BitmapCodec.EncodePng(target);
    }

    public byte[] Rotate(byte[] imageBytes, RotateFlipType rotateFlip)
    {
        using var bitmap = BitmapCodec.Decode(imageBytes);
        bitmap.RotateFlip(rotateFlip);
        return BitmapCodec.EncodePng(bitmap);
    }

    public ColorValue Sample(byte[] imageBytes, int x, int y)
    {
        using var bitmap = BitmapCodec.Decode(imageBytes);
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        var color = bitmap.GetPixel(x, y);
        return ColorValue.FromRgb(color.R, color.G, color.B, color.A);
    }
}
