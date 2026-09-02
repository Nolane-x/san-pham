using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Utilities;

namespace Magic.Capture.App.Utilities;

internal sealed record ImageMetadataReport(
    int Width,
    int Height,
    float HorizontalDpi,
    float VerticalDpi,
    string PixelFormat,
    long ByteLength,
    IReadOnlyDictionary<string, string> Properties,
    string Sha256,
    string Sha1,
    string Md5);

internal sealed class MetadataService
{
    public ImageMetadataReport Inspect(byte[] imageBytes)
    {
        using var bitmap = BitmapCodec.Decode(imageBytes);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in bitmap.PropertyItems.Take(128))
        {
            var key = $"EXIF 0x{item.Id:X4}";
            properties[key] = PropertyValue(item);
        }
        return new ImageMetadataReport(
            bitmap.Width,
            bitmap.Height,
            bitmap.HorizontalResolution,
            bitmap.VerticalResolution,
            bitmap.PixelFormat.ToString(),
            imageBytes.LongLength,
            properties,
            HashUtility.ComputeSha256(imageBytes),
            HashUtility.ComputeSha1(imageBytes),
            HashUtility.ComputeMd5(imageBytes));
    }

    private static string PropertyValue(PropertyItem item)
    {
        if (item.Value is not { Length: > 0 } value) return string.Empty;
        if (item.Type == 2)
            return System.Text.Encoding.UTF8.GetString(value).TrimEnd('\0');
        if (value.Length <= 32) return Convert.ToHexString(value);
        return Convert.ToHexString(value.AsSpan(0, 32)) + "…";
    }
}
