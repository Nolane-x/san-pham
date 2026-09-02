using System.Drawing;
using System.Drawing.Imaging;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed record ImageCompareResult(
    byte[] DifferencePng,
    byte[] HeatmapPng,
    byte[] MaskPng,
    byte[] AlignedSecondPng,
    int CanvasWidth,
    int CanvasHeight,
    long ChangedPixelCount,
    long ComparedPixelCount,
    long TotalPixelCount,
    double ChangedPixelPercent,
    double MeanAbsoluteDifference,
    double MeanBlueDifference,
    double MeanGreenDifference,
    double MeanRedDifference,
    double MeanSquaredError,
    double PeakSignalToNoiseRatio,
    double StructuralSimilarity,
    int PerceptualHashDistance,
    bool ContentRegistered,
    int AlignmentOffsetX,
    int AlignmentOffsetY,
    double AlignmentError);

internal sealed class ImageCompareService
{
    public ImageCompareResult Compare(
        byte[] firstImage,
        byte[] secondImage,
        ImageDifferenceOptions? options = null,
        bool autoAlignTranslation = false,
        bool autoRegisterContent = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(firstImage);
        ArgumentNullException.ThrowIfNull(secondImage);
        options = (options ?? new ImageDifferenceOptions()).Normalize();
        cancellationToken.ThrowIfCancellationRequested();

        int width;
        int height;
        byte[] pixelsA;
        byte[] pixelsB;
        using (var first = BitmapCodec.DecodeForCompare(firstImage))
        using (var second = BitmapCodec.DecodeForCompare(secondImage))
        {
            width = Math.Max(first.Width, second.Width);
            height = Math.Max(first.Height, second.Height);
            ImageWorkloadLimits.ValidateCompareDimensions(width, height);
            pixelsA = BitmapPixelBuffer.ReadBgraCanvas(first, width, height, cancellationToken: cancellationToken);
            if (autoRegisterContent)
            {
                var firstBounds = BitmapContentBounds.Find(first, cancellationToken: cancellationToken);
                var secondBounds = BitmapContentBounds.Find(second, cancellationToken: cancellationToken);
                using var registered = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(registered))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(second,
                        new Rectangle(firstBounds.X, firstBounds.Y, firstBounds.Width, firstBounds.Height),
                        new Rectangle(secondBounds.X, secondBounds.Y, secondBounds.Width, secondBounds.Height),
                        GraphicsUnit.Pixel);
                }
                pixelsB = BitmapPixelBuffer.ReadBgraCanvas(registered, width, height, cancellationToken: cancellationToken);
            }
            else pixelsB = BitmapPixelBuffer.ReadBgraCanvas(second, width, height, cancellationToken: cancellationToken);
        }

        TranslationAlignmentResult alignment;
        if (autoAlignTranslation)
        {
            alignment = TranslationAlignment.FindBestBgra(pixelsA, pixelsB, width, height, maxOffset: 32, sampleStep: 4, cancellationToken: cancellationToken);
            if (alignment.OffsetX != 0 || alignment.OffsetY != 0)
                BgraTranslation.TranslateInPlace(pixelsB, width, height, alignment.OffsetX, alignment.OffsetY);
        }
        else
        {
            alignment = new TranslationAlignmentResult(0, 0, 0, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stats = ImageDifference.AnalyzeBgra(pixelsA, pixelsB, options, cancellationToken);
        var metrics = ImageComparisonMetrics.CalculateBgra(pixelsA, pixelsB, cancellationToken);
        var hashA = PerceptualHash.ComputeDHashBgra(pixelsA, width, height);
        var hashB = PerceptualHash.ComputeDHashBgra(pixelsB, width, height);
        var perceptualDistance = PerceptualHash.HammingDistance(hashA, hashB);

        // Produce maps sequentially. A 5K canvas is tens of MB in BGRA; reusing one map buffer
        // avoids retaining three additional full-size arrays. Source bitmaps have already been
        // disposed, so the heavy comparison phase holds only the two source buffers plus this map.
        var mapBuffer = new byte[pixelsA.Length];
        var differencePng = EncodeDifferenceMap(pixelsA, pixelsB, options, mapBuffer, DifferenceMapKind.Difference, width, height, cancellationToken);
        var heatmapPng = EncodeDifferenceMap(pixelsA, pixelsB, options, mapBuffer, DifferenceMapKind.Heatmap, width, height, cancellationToken);
        var maskPng = EncodeDifferenceMap(pixelsA, pixelsB, options, mapBuffer, DifferenceMapKind.Mask, width, height, cancellationToken);
        using var alignedSecond = CreateBitmap(width, height, pixelsB);
        var alignedSecondPng = BitmapCodec.EncodePng(alignedSecond);

        return new ImageCompareResult(
            differencePng,
            heatmapPng,
            maskPng,
            alignedSecondPng,
            width,
            height,
            stats.ChangedPixelCount,
            stats.ComparedPixelCount,
            stats.PixelCount,
            stats.ChangedPixelPercent,
            stats.MeanAbsoluteDifference,
            stats.MeanBlueDifference,
            stats.MeanGreenDifference,
            stats.MeanRedDifference,
            metrics.MeanSquaredError,
            metrics.PeakSignalToNoiseRatio,
            metrics.StructuralSimilarity,
            perceptualDistance,
            autoRegisterContent,
            alignment.OffsetX,
            alignment.OffsetY,
            alignment.MeanAbsoluteError);
    }

    private enum DifferenceMapKind
    {
        Difference,
        Heatmap,
        Mask
    }

    private static byte[] EncodeDifferenceMap(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ImageDifferenceOptions options,
        byte[] output,
        DifferenceMapKind kind,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        RenderDifferenceMap(first, second, options, output, kind, cancellationToken);
        using var bitmap = CreateBitmap(width, height, output);
        return BitmapCodec.EncodePng(bitmap);
    }

    private static void RenderDifferenceMap(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ImageDifferenceOptions options,
        Span<byte> output,
        DifferenceMapKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.Clear();
        for (var index = 0; index < first.Length; index += 4)
        {
            if ((index & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var bothTransparent = first[index + 3] == 0 && second[index + 3] == 0;
            if (options.IgnoreFullyTransparent && bothTransparent)
            {
                output[index + 3] = 0;
                continue;
            }

            var db = Math.Abs(first[index] - second[index]);
            var dg = Math.Abs(first[index + 1] - second[index + 1]);
            var dr = Math.Abs(first[index + 2] - second[index + 2]);
            var da = Math.Abs(first[index + 3] - second[index + 3]);
            var max = Math.Max(db, Math.Max(dg, dr));
            if (!options.IgnoreAlpha) max = Math.Max(max, da);
            var changed = max > options.Threshold;
            var intensity = (byte)Math.Clamp((db + dg + dr) / 3, 0, 255);

            switch (kind)
            {
                case DifferenceMapKind.Difference:
                    output[index] = intensity;
                    output[index + 1] = intensity;
                    output[index + 2] = intensity;
                    break;
                case DifferenceMapKind.Heatmap:
                    HeatmapColor(intensity, out var hb, out var hg, out var hr);
                    output[index] = hb;
                    output[index + 1] = hg;
                    output[index + 2] = hr;
                    break;
                case DifferenceMapKind.Mask:
                    var maskValue = changed ? (byte)255 : (byte)0;
                    output[index] = maskValue;
                    output[index + 1] = maskValue;
                    output[index + 2] = maskValue;
                    break;
            }
            output[index + 3] = 255;
        }
    }

    private static void HeatmapColor(byte intensity, out byte b, out byte g, out byte r)
    {
        // Black -> blue -> cyan -> yellow -> red. Deterministic and cheap; no lookup allocation.
        var t = intensity / 255d;
        r = (byte)Math.Clamp((int)Math.Round(255 * Math.Min(1, Math.Max(0, 2 * t - .25))), 0, 255);
        g = (byte)Math.Clamp((int)Math.Round(255 * Math.Min(1, Math.Max(0, 2 - Math.Abs(4 * t - 2)))), 0, 255);
        b = (byte)Math.Clamp((int)Math.Round(255 * Math.Min(1, Math.Max(0, 1.5 - 2 * t))), 0, 255);
    }

    private static Bitmap CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapPixelBuffer.WriteBgra(bitmap, pixels);
        return bitmap;
    }

}
