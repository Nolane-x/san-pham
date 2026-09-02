namespace Magic.Capture.Core.Capture;

public static class PngDimensions
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryRead(ReadOnlySpan<byte> pngBytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (pngBytes.Length < 24 || !pngBytes[..8].SequenceEqual(Signature)) return false;
        if (pngBytes[12] != (byte)'I' || pngBytes[13] != (byte)'H' || pngBytes[14] != (byte)'D' || pngBytes[15] != (byte)'R') return false;

        var w = ReadUInt32BigEndian(pngBytes.Slice(16, 4));
        var h = ReadUInt32BigEndian(pngBytes.Slice(20, 4));
        if (w is 0 or > int.MaxValue || h is 0 or > int.MaxValue) return false;
        width = (int)w;
        height = (int)h;
        return true;
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> value) =>
        ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];
}
