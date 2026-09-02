using System.Drawing;
using System.Drawing.Imaging;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal static class BitmapCodec
{
    public static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public static Bitmap Decode(byte[] imageBytes) => DecodeCore(imageBytes, pixelProcessing: false, compare: false);

    public static Bitmap DecodeForPixelProcessing(byte[] imageBytes) => DecodeCore(imageBytes, pixelProcessing: true, compare: false);

    public static Bitmap DecodeForCompare(byte[] imageBytes) => DecodeCore(imageBytes, pixelProcessing: false, compare: true);

    private static Bitmap DecodeCore(byte[] imageBytes, bool pixelProcessing, bool compare)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ImageWorkloadLimits.ValidateEncodedLength(imageBytes.LongLength);
        using var stream = new MemoryStream(imageBytes, writable: false);
        using var source = new Bitmap(stream);
        ImageWorkloadLimits.ValidateDimensions(source.Width, source.Height);
        if (compare) ImageWorkloadLimits.ValidateCompareDimensions(source.Width, source.Height);
        else if (pixelProcessing) ImageWorkloadLimits.ValidatePixelProcessingDimensions(source.Width, source.Height);
        return new Bitmap(source);
    }

    public static byte[] CropPng(byte[] sourcePng, PixelRect bounds)
    {
        using var source = Decode(sourcePng);
        var safe = bounds.Intersect(new PixelRect(0, 0, source.Width, source.Height));
        if (safe.IsEmpty) throw new ArgumentOutOfRangeException(nameof(bounds), "Crop bounds do not intersect the source image.");
        using var cropped = source.Clone(new Rectangle(safe.X, safe.Y, safe.Width, safe.Height), PixelFormat.Format32bppArgb);
        return EncodePng(cropped);
    }

    public static byte[] EncodeJpeg(Bitmap bitmap, long jpegQuality = 92)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(bitmap.Width, bitmap.Height);
        using var flattened = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(flattened))
        {
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(bitmap, 0, 0);
        }
        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(jpegQuality, 1, 100));
        flattened.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    public static byte[] Transcode(byte[] sourceBytes, ImageFormat format, long jpegQuality = 92)
    {
        using var bitmap = Decode(sourceBytes);
        if (format.Guid == ImageFormat.Jpeg.Guid) return EncodeJpeg(bitmap, jpegQuality);
        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }
}
