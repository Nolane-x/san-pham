namespace Magic.Capture.Core.Recording;

public readonly record struct ApngFrameDelay(ushort Numerator, ushort Denominator);

public static class ApngEncodingPolicy
{
    private static readonly uint[] Table = BuildTable();

    public static ApngFrameDelay FrameDelay(int framesPerSecond)
    {
        var fps = Math.Clamp(framesPerSecond, RecordingRules.MinimumFramesPerSecond, RecordingRules.MaximumFramesPerSecond);
        var milliseconds = Math.Clamp((int)Math.Round(1000.0 / fps), 1, ushort.MaxValue);
        return new ApngFrameDelay((ushort)milliseconds, 1000);
    }

    public static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
