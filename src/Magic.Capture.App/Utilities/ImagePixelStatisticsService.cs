using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Utilities;

internal sealed class ImagePixelStatisticsService
{
    public PixelStatisticsResult Compute(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var decoded = BitmapCodec.DecodeForPixelProcessing(imageBytes);
        using var argb = decoded.Clone(new System.Drawing.Rectangle(0, 0, decoded.Width, decoded.Height), PixelFormat.Format32bppArgb);
        var pixels = BitmapPixelBuffer.ReadBgra(argb);
        return PixelStatistics.ComputeBgra(pixels, argb.Width, argb.Height);
    }
}
