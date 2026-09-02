using System.Drawing;
using System.Security.Cryptography;
using Magic.Capture.Core.History;

namespace Magic.Capture.App.Imaging;

internal static class ImageFingerprintService
{
    public static string ComputeSha256(ReadOnlySpan<byte> encodedBytes) =>
        Convert.ToHexString(SHA256.HashData(encodedBytes)).ToLowerInvariant();

    public static ulong ComputeDifferenceHash64(byte[] encodedBytes)
    {
        ArgumentNullException.ThrowIfNull(encodedBytes);
        using var source = BitmapCodec.Decode(encodedBytes);
        using var reduced = new Bitmap(source, new Size(9, 8));
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            var previous = Luma(reduced.GetPixel(0, y));
            for (var x = 1; x < 9; x++)
            {
                var current = Luma(reduced.GetPixel(x, y));
                if (previous > current) hash |= 1UL << bit;
                previous = current;
                bit++;
            }
        }
        return hash;
    }

    public static (string Sha256, ulong? DHash64) Compute(byte[] encodedBytes)
    {
        ArgumentNullException.ThrowIfNull(encodedBytes);
        var sha = ComputeSha256(encodedBytes);
        try { return (sha, ComputeDifferenceHash64(encodedBytes)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Runtime.InteropServices.ExternalException)
        {
            return (sha, null);
        }
    }

    private static int Luma(Color color) => (299 * color.R + 587 * color.G + 114 * color.B) / 1000;
}
