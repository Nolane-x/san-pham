using Magic.Capture.App.Imaging;
using ZXing;
using ZXing.Common;

namespace Magic.Capture.App.Analysis;

internal sealed class BarcodeService
{
    public IReadOnlyList<BarcodeHit> Decode(byte[] imageBytes)
    {
        using var bitmap = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        var pixels = BitmapCodec.CopyBgra32Pixels(bitmap);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PureBarcode = false
            }
        };

        var results = reader.DecodeMultiple(
            pixels,
            bitmap.Width,
            bitmap.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32);
        if (results is null || results.Length == 0)
        {
            var single = reader.Decode(
                pixels,
                bitmap.Width,
                bitmap.Height,
                RGBLuminanceSource.BitmapFormat.BGRA32);
            return single is null ? [] : [ToHit(single)];
        }

        return results
            .Where(result => result is not null)
            .Select(ToHit)
            .GroupBy(hit => (hit.Format, hit.Text))
            .Select(group => group.First())
            .ToArray();
    }

    private static BarcodeHit ToHit(Result result)
    {
        Magic.Capture.Core.Geometry.PixelRect? bounds = null;
        if (result.ResultPoints is { Length: > 0 } points)
        {
            var minX = (int)Math.Floor(points.Min(p => p.X));
            var minY = (int)Math.Floor(points.Min(p => p.Y));
            var maxX = (int)Math.Ceiling(points.Max(p => p.X));
            var maxY = (int)Math.Ceiling(points.Max(p => p.Y));
            bounds = new Magic.Capture.Core.Geometry.PixelRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
        return new BarcodeHit(result.BarcodeFormat.ToString(), result.Text ?? string.Empty, result.RawBytes, bounds);
    }
}
