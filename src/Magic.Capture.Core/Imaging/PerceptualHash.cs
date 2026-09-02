namespace Magic.Capture.Core.Imaging;

public static class PerceptualHash
{
    public static ulong ComputeDHashBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("BGRA buffer dimensions do not match the supplied image size.");
        Span<byte> samples = stackalloc byte[72]; // 9 x 8
        for (var sy = 0; sy < 8; sy++)
        {
            var y = Math.Clamp((int)Math.Round((sy + .5) * height / 8d - .5), 0, height - 1);
            for (var sx = 0; sx < 9; sx++)
            {
                var x = Math.Clamp((int)Math.Round((sx + .5) * width / 9d - .5), 0, width - 1);
                var i = (y * width + x) * 4;
                samples[sy * 9 + sx] = (byte)((bgra[i + 2] * 77 + bgra[i + 1] * 150 + bgra[i] * 29) >> 8);
            }
        }

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++, bit++)
                if (samples[y * 9 + x] > samples[y * 9 + x + 1]) hash |= 1UL << bit;
        return hash;
    }

    public static int HammingDistance(ulong first, ulong second) => System.Numerics.BitOperations.PopCount(first ^ second);
}
