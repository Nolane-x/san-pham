using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal sealed record HorizontalStitchPairResult(int LeftIndex, int RightIndex, HorizontalOverlapMatch Match);
internal sealed record HorizontalStitchResult(byte[] PngBytes, IReadOnlyList<HorizontalStitchPairResult> Pairs);

internal sealed class HorizontalImageStitcher
{
    public HorizontalOverlapMatch? FindPairOverlap(byte[] leftPng, byte[] rightPng, HorizontalOverlapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(leftPng);
        ArgumentNullException.ThrowIfNull(rightPng);
        using var left = BitmapCodec.Decode(leftPng);
        using var right = BitmapCodec.Decode(rightPng);
        if (left.Height != right.Height) return null;
        return HorizontalOverlapMatcher.Find(ToGray(left), left.Width, ToGray(right), right.Width, left.Height, options);
    }

    public HorizontalStitchResult Stitch(IReadOnlyList<byte[]> frames, HorizontalOverlapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count < 2) throw new ArgumentException("At least two frames are required.", nameof(frames));
        options ??= new HorizontalOverlapOptions();

        var dimensions = new (int Width, int Height)[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            if (!PngDimensions.TryRead(frames[i], out var width, out var height))
                throw new InvalidDataException($"Horizontal stitch frame {i + 1} is not a valid PNG.");
            ImageWorkloadLimits.ValidateDimensions(width, height);
            dimensions[i] = (width, height);
        }

        var outputHeight = dimensions[0].Height;
        if (dimensions.Any(size => size.Height != outputHeight))
            throw new InvalidOperationException("All horizontal stitch frames must have the same pixel height.");

        var pairs = new List<HorizontalStitchPairResult>(frames.Count - 1);
        var overlaps = new int[frames.Count - 1];
        for (var i = 0; i < frames.Count - 1; i++)
        {
            var match = FindPairOverlap(frames[i], frames[i + 1], options)
                ?? throw new InvalidOperationException($"Could not find a reliable horizontal overlap between frames {i + 1} and {i + 2}. Try a smaller horizontal scroll step or pause animated content.");
            overlaps[i] = match.OverlapColumns;
            pairs.Add(new HorizontalStitchPairResult(i, i + 1, match));
        }

        long outputWidth = dimensions[0].Width;
        for (var i = 1; i < dimensions.Length; i++)
            outputWidth = checked(outputWidth + Math.Max(0, dimensions[i].Width - overlaps[i - 1]));
        ImageWorkloadLimits.ValidateDimensions(checked((int)outputWidth), outputHeight);

        using var output = new Bitmap((int)outputWidth, outputHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            var x = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                using var frame = BitmapCodec.Decode(frames[i]);
                var overlap = i == 0 ? 0 : overlaps[i - 1];
                var sourceX = Math.Clamp(overlap, 0, frame.Width);
                var sourceWidth = frame.Width - sourceX;
                if (sourceWidth <= 0) continue;
                var sourceRect = new Rectangle(sourceX, 0, sourceWidth, frame.Height);
                var destinationRect = new Rectangle(x, 0, sourceWidth, frame.Height);
                graphics.DrawImage(frame, destinationRect, sourceRect, GraphicsUnit.Pixel);
                x = checked(x + sourceWidth);
            }
        }

        return new HorizontalStitchResult(BitmapCodec.EncodePng(output), pairs);
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
                    gray[y * copy.Width + x] = (byte)((raw[offset + 2] * 77 + raw[offset + 1] * 150 + raw[offset] * 29) >> 8);
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
