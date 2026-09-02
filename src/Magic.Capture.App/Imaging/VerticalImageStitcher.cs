using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed record StitchFrameTrim(int TopRows = 0, int BottomRows = 0);
internal sealed record StitchPairResult(int UpperIndex, int LowerIndex, OverlapMatch Match);
internal sealed record StitchResult(byte[] PngBytes, IReadOnlyList<StitchPairResult> Pairs);

internal sealed class VerticalImageStitcher
{
    private const long MaxOutputPixels = 150_000_000;
    private const int MaxOutputDimension = 200_000;

    public OverlapMatch? FindPairOverlap(
        byte[] upperPng,
        byte[] lowerPng,
        StitchFrameTrim? upperTrim = null,
        StitchFrameTrim? lowerTrim = null,
        VerticalOverlapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(upperPng);
        ArgumentNullException.ThrowIfNull(lowerPng);
        options ??= new VerticalOverlapOptions();
        using var upper = BitmapCodec.Decode(upperPng);
        using var lower = BitmapCodec.Decode(lowerPng);
        if (upper.Width != lower.Width) return null;

        var normalizedUpper = NormalizeTrim(upperTrim, upper.Height);
        var normalizedLower = NormalizeTrim(lowerTrim, lower.Height);
        var upperGray = ToGray(upper);
        var lowerGray = ToGray(lower);
        return VerticalOverlapMatcher.FindTrimmed(
            upperGray, upper.Height,
            lowerGray, lower.Height,
            upper.Width,
            normalizedUpper.TopRows, normalizedUpper.BottomRows,
            normalizedLower.TopRows, normalizedLower.BottomRows,
            options);
    }

    public StitchResult Stitch(
        IReadOnlyList<byte[]> frames,
        VerticalOverlapOptions? options = null,
        IReadOnlyList<StitchFrameTrim>? trims = null)
    {
        if (frames.Count < 2) throw new ArgumentException("At least two frames are required.", nameof(frames));
        if (trims is not null && trims.Count != frames.Count)
            throw new ArgumentException("When frame trims are supplied there must be exactly one trim per frame.", nameof(trims));
        options ??= new VerticalOverlapOptions();

        var dimensions = new (int Width, int Height)[frames.Count];
        var normalizedTrims = new StitchFrameTrim[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            if (!PngDimensions.TryRead(frames[i], out var width, out var height))
                throw new InvalidDataException($"Stitch frame {i + 1} is not a valid PNG.");
            dimensions[i] = (width, height);
            normalizedTrims[i] = NormalizeTrim(trims?[i], height);
        }
        var outputWidth = dimensions[0].Width;
        if (dimensions.Any(size => size.Width != outputWidth))
            throw new InvalidOperationException("All stitch frames must have the same pixel width.");

        // Match pair-by-pair and dispose decoded bitmaps immediately. Long captures therefore keep
        // roughly two source frames in memory instead of every frame in the session.
        var pairs = new List<StitchPairResult>();
        var overlaps = new int[frames.Count - 1];
        for (var i = 0; i < frames.Count - 1; i++)
        {
            using var upper = BitmapCodec.Decode(frames[i]);
            using var lower = BitmapCodec.Decode(frames[i + 1]);
            var upperGray = ToGray(upper);
            var lowerGray = ToGray(lower);
            var upperTrim = normalizedTrims[i];
            var lowerTrim = normalizedTrims[i + 1];
            var match = VerticalOverlapMatcher.FindTrimmed(
                upperGray, upper.Height,
                lowerGray, lower.Height,
                outputWidth,
                upperTrim.TopRows, upperTrim.BottomRows,
                lowerTrim.TopRows, lowerTrim.BottomRows,
                options)
                ?? throw new InvalidOperationException($"Could not find a reliable overlap between frames {i + 1} and {i + 2}. Try a smaller scroll step or pause animated content.");
            overlaps[i] = match.OverlapRows;
            pairs.Add(new StitchPairResult(i, i + 1, match));
        }

        long totalHeight = FrameContributionHeight(dimensions[0].Height, normalizedTrims[0], overlapRows: 0);
        for (var i = 1; i < frames.Count; i++)
            totalHeight = checked(totalHeight + FrameContributionHeight(dimensions[i].Height, normalizedTrims[i], overlaps[i - 1]));
        if (totalHeight <= 0)
            throw new InvalidOperationException("The stitch trims removed all image content.");
        if (outputWidth > MaxOutputDimension || totalHeight > MaxOutputDimension || checked((long)outputWidth * totalHeight) > MaxOutputPixels)
            throw new InvalidOperationException("The stitched image exceeds Magic Capture Desktop's safe in-memory image limit. Capture a smaller region or split the page into sections.");

        using var output = new Bitmap(outputWidth, (int)totalHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            var y = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                using var frame = BitmapCodec.Decode(frames[i]);
                var trim = normalizedTrims[i];
                var overlap = i == 0 ? 0 : overlaps[i - 1];
                var sourceY = trim.TopRows + overlap;
                var sourceHeight = frame.Height - trim.BottomRows - sourceY;
                if (sourceHeight <= 0) continue;
                var sourceRect = new Rectangle(0, sourceY, frame.Width, sourceHeight);
                var destinationRect = new Rectangle(0, y, sourceRect.Width, sourceRect.Height);
                graphics.DrawImage(frame, destinationRect, sourceRect, GraphicsUnit.Pixel);
                y += sourceHeight;
            }
        }
        return new StitchResult(BitmapCodec.EncodePng(output), pairs);
    }

    private static int FrameContributionHeight(int height, StitchFrameTrim trim, int overlapRows)
    {
        var contribution = height - trim.TopRows - trim.BottomRows - Math.Max(0, overlapRows);
        return Math.Max(0, contribution);
    }

    private static StitchFrameTrim NormalizeTrim(StitchFrameTrim? trim, int height)
    {
        if (trim is null || height <= 1) return new StitchFrameTrim();
        var top = Math.Clamp(trim.TopRows, 0, height - 1);
        var bottom = Math.Clamp(trim.BottomRows, 0, height - 1);
        if (top + bottom >= height) return new StitchFrameTrim();
        return new StitchFrameTrim(top, bottom);
    }

    private static byte[] ToGray(Bitmap bitmap)
    {
        using var copy = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(copy)) graphics.DrawImageUnscaled(bitmap, 0, 0);
        var rect = new Rectangle(0, 0, copy.Width, copy.Height);
        var data = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var raw = new byte[checked(stride * copy.Height)];
            for (var y = 0; y < copy.Height; y++)
            {
                var source = IntPtr.Add(data.Scan0, y * data.Stride);
                Marshal.Copy(source, raw, y * stride, copy.Width * 3);
            }
            var gray = new byte[checked(copy.Width * copy.Height)];
            for (var y = 0; y < copy.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < copy.Width; x++)
                {
                    var offset = row + x * 3;
                    var b = raw[offset];
                    var g = raw[offset + 1];
                    var r = raw[offset + 2];
                    gray[y * copy.Width + x] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
                }
            }
            return gray;
        }
        finally
        {
            copy.UnlockBits(data);
        }
    }
}
