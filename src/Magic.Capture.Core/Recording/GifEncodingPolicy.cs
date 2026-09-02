namespace Magic.Capture.Core.Recording;

public static class GifEncodingPolicy
{
    public const int PaletteSize = 256;
    public const int MinimumCodeSize = 8;
    public const int MaximumLzwCode = 4095;

    public static byte ToPaletteIndex(byte red, byte green, byte blue) =>
        (byte)(((red >> 5) << 5) | ((green >> 5) << 2) | (blue >> 6));

    public static byte[] BuildRgb332Palette()
    {
        var palette = new byte[PaletteSize * 3];
        for (var index = 0; index < PaletteSize; index++)
        {
            var r = (index >> 5) & 0x07;
            var g = (index >> 2) & 0x07;
            var b = index & 0x03;
            var o = index * 3;
            palette[o] = (byte)Math.Round(r * 255.0 / 7.0);
            palette[o + 1] = (byte)Math.Round(g * 255.0 / 7.0);
            palette[o + 2] = (byte)Math.Round(b * 255.0 / 3.0);
        }
        return palette;
    }

    public static int FrameDelayHundredths(int framesPerSecond)
    {
        var fps = Math.Clamp(framesPerSecond, RecordingRules.MinimumFramesPerSecond, RecordingRules.MaximumFramesPerSecond);
        return Math.Max(1, (int)Math.Round(100.0 / fps));
    }

    public static byte[] EncodeLzw(ReadOnlySpan<byte> indexes)
    {
        if (indexes.IsEmpty) throw new ArgumentException("GIF frame has no palette indexes.", nameof(indexes));

        const int clearCode = 1 << MinimumCodeSize;
        const int endCode = clearCode + 1;
        var nextCode = endCode + 1;
        var codeSize = MinimumCodeSize + 1;
        var dictionary = new Dictionary<int, int>(4096);
        var writer = new LsbBitWriter(Math.Max(32, indexes.Length / 2));

        void Emit(int code) => writer.Write(code, codeSize);
        void ResetDictionary()
        {
            dictionary.Clear();
            nextCode = endCode + 1;
            codeSize = MinimumCodeSize + 1;
        }

        Emit(clearCode);
        var prefix = (int)indexes[0];
        for (var i = 1; i < indexes.Length; i++)
        {
            var suffix = indexes[i];
            var key = (prefix << 8) | suffix;
            if (dictionary.TryGetValue(key, out var combined))
            {
                prefix = combined;
                continue;
            }

            Emit(prefix);
            if (nextCode <= MaximumLzwCode)
            {
                dictionary[key] = nextCode++;
                if (nextCode > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clearCode);
                ResetDictionary();
            }
            prefix = suffix;
        }
        Emit(prefix);
        Emit(endCode);
        return writer.ToArray();
    }

    private sealed class LsbBitWriter
    {
        private readonly List<byte> _bytes;
        private uint _buffer;
        private int _bitCount;

        public LsbBitWriter(int capacity) => _bytes = new List<byte>(capacity);

        public void Write(int value, int bits)
        {
            _buffer |= (uint)value << _bitCount;
            _bitCount += bits;
            while (_bitCount >= 8)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer >>= 8;
                _bitCount -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_bitCount > 0)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer = 0;
                _bitCount = 0;
            }
            return _bytes.ToArray();
        }
    }
}
