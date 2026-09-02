using Magic.Capture.Core.Recording;

namespace Magic.Capture.Core.Tests;

public sealed class AnimatedRecordingEncodingPolicyTests
{
    [Theory]
    [InlineData(255, 0, 0, 0b11100000)]
    [InlineData(0, 255, 0, 0b00011100)]
    [InlineData(0, 0, 255, 0b00000011)]
    [InlineData(255, 255, 255, 255)]
    public void GifPalette_MapsRgbToDeterministic332(byte r, byte g, byte b, byte expected)
    {
        Assert.Equal(expected, GifEncodingPolicy.ToPaletteIndex(r, g, b));
    }

    [Theory]
    [InlineData(5, 20)]
    [InlineData(25, 4)]
    [InlineData(60, 2)]
    public void GifDelay_IsAtLeastOneHundredth(int fps, int expectedHundredths)
    {
        Assert.Equal(expectedHundredths, GifEncodingPolicy.FrameDelayHundredths(fps));
    }

    [Fact]
    public void GifLzw_ProducesBoundedNonEmptyPayload()
    {
        var indexes = Enumerable.Repeat((byte)42, 1024).ToArray();
        var encoded = GifEncodingPolicy.EncodeLzw(indexes);
        Assert.NotEmpty(encoded);
        Assert.True(encoded.Length < indexes.Length);
    }


    [Fact]
    public void GifLzw_CrossesCodeSizeBoundariesWithoutCorruption()
    {
        var indexes = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
        var encoded = GifEncodingPolicy.EncodeLzw(indexes);
        var decoded = DecodeGifLzw(encoded, GifEncodingPolicy.MinimumCodeSize);
        Assert.Equal(indexes, decoded);
    }

    [Fact]
    public void PngCrc32_MatchesStandardVector()
    {
        Assert.Equal(0xCBF43926u, ApngEncodingPolicy.Crc32("123456789"u8));
    }

    [Theory]
    [InlineData(30, 33, 1000)]
    [InlineData(60, 17, 1000)]
    public void ApngDelay_UsesMillisecondRational(int fps, ushort expectedNumerator, ushort expectedDenominator)
    {
        var delay = ApngEncodingPolicy.FrameDelay(fps);
        Assert.Equal(expectedNumerator, delay.Numerator);
        Assert.Equal(expectedDenominator, delay.Denominator);
    }
    private static byte[] DecodeGifLzw(byte[] encoded, int minimumCodeSize)
    {
        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var codeSize = minimumCodeSize + 1;
        var nextCode = endCode + 1;
        var bitOffset = 0;
        var table = new Dictionary<int, byte[]>(4096);
        ResetTable();
        byte[]? previous = null;
        var output = new List<byte>();

        while (true)
        {
            var code = ReadCode(codeSize);
            if (code == clearCode)
            {
                ResetTable();
                codeSize = minimumCodeSize + 1;
                nextCode = endCode + 1;
                previous = null;
                continue;
            }
            if (code == endCode) break;

            byte[] entry;
            if (table.TryGetValue(code, out var known))
            {
                entry = known;
            }
            else if (code == nextCode && previous is not null)
            {
                entry = previous.Concat(new[] { previous[0] }).ToArray();
            }
            else
            {
                throw new InvalidDataException($"Invalid GIF LZW code {code} at dictionary index {nextCode}.");
            }

            output.AddRange(entry);
            if (previous is not null && nextCode <= GifEncodingPolicy.MaximumLzwCode)
            {
                table[nextCode++] = previous.Concat(new[] { entry[0] }).ToArray();
                if (nextCode == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            previous = entry;
        }

        return output.ToArray();

        int ReadCode(int bits)
        {
            var value = 0;
            for (var bit = 0; bit < bits; bit++)
            {
                var byteIndex = bitOffset >> 3;
                if (byteIndex >= encoded.Length) throw new EndOfStreamException("GIF LZW stream ended before an end code.");
                value |= ((encoded[byteIndex] >> (bitOffset & 7)) & 1) << bit;
                bitOffset++;
            }
            return value;
        }

        void ResetTable()
        {
            table.Clear();
            for (var value = 0; value < clearCode; value++) table[value] = new[] { checked((byte)value) };
        }
    }

}
