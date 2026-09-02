using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Export;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Utilities;

internal sealed record ImageOptimizationResult(
    byte[] Bytes,
    int Width,
    int Height,
    int? JpegQuality,
    long OriginalBytes,
    bool TargetMet)
{
    public long SavedBytes => Math.Max(0, OriginalBytes - Bytes.LongLength);
    public double SavedPercent => OriginalBytes <= 0 ? 0 : SavedBytes * 100d / OriginalBytes;
}

internal sealed class ImageOptimizationService
{
    public ImageOptimizationResult CompressJpeg(byte[] sourceBytes, int quality)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        using var bitmap = BitmapCodec.DecodeForPixelProcessing(sourceBytes);
        var normalizedQuality = Math.Clamp(quality, 1, 100);
        var bytes = BitmapCodec.EncodeJpeg(bitmap, normalizedQuality);
        return new ImageOptimizationResult(bytes, bitmap.Width, bitmap.Height, normalizedQuality, sourceBytes.LongLength, true);
    }

    public ImageOptimizationResult CompressJpegToTarget(byte[] sourceBytes, ImageOptimizationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        policy = policy.Normalize();
        using var source = BitmapCodec.DecodeForPixelProcessing(sourceBytes);
        Bitmap working = new(source);
        try
        {
            byte[]? smallest = null;
            var smallestQuality = policy.MinimumJpegQuality;
            for (var resizePass = 0; resizePass < 7; resizePass++)
            {
                var encoded = SearchQuality(working, policy, out var quality, out var minQualityBytes);
                if (encoded is not null)
                    return new ImageOptimizationResult(encoded, working.Width, working.Height, quality, sourceBytes.LongLength, true);

                smallest = minQualityBytes;
                smallestQuality = policy.MinimumJpegQuality;
                if (working.Width <= 64 || working.Height <= 64) break;

                var scale = ImageOptimizationPolicy.ResizeScale(minQualityBytes.LongLength, policy.TargetBytes);
                var nextWidth = Math.Clamp((int)Math.Floor(working.Width * scale), 64, policy.MaxDimension);
                var nextHeight = Math.Clamp((int)Math.Floor(working.Height * scale), 64, policy.MaxDimension);
                if (nextWidth >= working.Width && nextHeight >= working.Height) break;
                var resized = ResizeBitmap(working, nextWidth, nextHeight);
                working.Dispose();
                working = resized;
            }

            smallest ??= BitmapCodec.EncodeJpeg(working, smallestQuality);
            return new ImageOptimizationResult(smallest, working.Width, working.Height, smallestQuality, sourceBytes.LongLength, smallest.LongLength <= policy.TargetBytes);
        }
        finally
        {
            working.Dispose();
        }
    }

    public ImageOptimizationResult OptimizePngLossless(byte[] sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        using var bitmap = BitmapCodec.Decode(sourceBytes);
        var encoded = BitmapCodec.EncodePng(bitmap);
        var result = encoded.LongLength < sourceBytes.LongLength ? encoded : sourceBytes.ToArray();
        return new ImageOptimizationResult(result, bitmap.Width, bitmap.Height, null, sourceBytes.LongLength, true);
    }

    public ImageOptimizationResult OptimizePngLossy(byte[] sourceBytes, int channelBits = 6)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        channelBits = Math.Clamp(channelBits, 3, 8);
        using var bitmap = BitmapCodec.DecodeForPixelProcessing(sourceBytes);
        using var argb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(argb)) graphics.DrawImageUnscaled(bitmap, 0, 0);
        var pixels = BitmapPixelBuffer.ReadBgra(argb);
        var discardedBits = 8 - channelBits;
        var mask = (byte)(0xFF << discardedBits);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] &= mask;
            pixels[i + 1] &= mask;
            pixels[i + 2] &= mask;
        }
        BitmapPixelBuffer.WriteBgra(argb, pixels);
        var encoded = BitmapCodec.EncodePng(argb);
        return new ImageOptimizationResult(encoded, argb.Width, argb.Height, null, sourceBytes.LongLength, true);
    }

    public byte[] Resize(byte[] sourceBytes, int width, int height)
    {
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        using var source = BitmapCodec.Decode(sourceBytes);
        using var resized = ResizeBitmap(source, width, height);
        return BitmapCodec.EncodePng(resized);
    }

    private static byte[]? SearchQuality(Bitmap bitmap, ImageOptimizationPolicy policy, out int quality, out byte[] minimumQualityBytes)
    {
        minimumQualityBytes = BitmapCodec.EncodeJpeg(bitmap, policy.MinimumJpegQuality);
        if (minimumQualityBytes.LongLength > policy.TargetBytes)
        {
            quality = policy.MinimumJpegQuality;
            return null;
        }

        var low = policy.MinimumJpegQuality;
        var high = policy.JpegQuality;
        quality = low;
        var best = minimumQualityBytes;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var bytes = BitmapCodec.EncodeJpeg(bitmap, mid);
            if (bytes.LongLength <= policy.TargetBytes)
            {
                best = bytes;
                quality = mid;
                low = mid + 1;
            }
            else high = mid - 1;
        }
        return best;
    }

    private static Bitmap ResizeBitmap(Bitmap source, int width, int height)
    {
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(width, height);
        var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return target;
    }
}
