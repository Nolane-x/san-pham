using System.Drawing;
using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Utilities;
using ZXing;
using ZXing.Common;

namespace Magic.Capture.App.Utilities;

internal sealed class BarcodeGeneratorService
{
    public byte[] GenerateQr(string text, int size = 512)
    {
        text = GeneratedCodeInputPolicy.NormalizeQr(text);
        size = Math.Clamp(size, 128, 2048);
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 2, PureBarcode = true }
        };
        return EncodePng(writer.Write(text));
    }

    public byte[] GenerateCode128(string text, int width = 900, int height = 260)
    {
        text = GeneratedCodeInputPolicy.NormalizeCode128(text);
        width = Math.Clamp(width, 240, 4096);
        height = Math.Clamp(height, 96, 1024);
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = width, Height = height, Margin = 12, PureBarcode = true }
        };
        return EncodePng(writer.Write(text));
    }

    private static byte[] EncodePng(ZXing.Rendering.PixelData pixels)
    {
        using var bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        BitmapPixelBuffer.WriteBgra(bitmap, pixels.Pixels);
        return BitmapCodec.EncodePng(bitmap);
    }
}
