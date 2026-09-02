using System.Buffers.Binary;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal sealed class GifRecordingEncoder
{
    public async Task EncodeAsync(
        string path,
        int width,
        int height,
        int framesPerSecond,
        Func<long, CancellationToken, Task<RecordingFramePixels?>> frameFactory,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("GIF output path must be fully qualified.", nameof(path));
        if (width is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(frameFactory);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await WriteHeaderAsync(stream, width, height, cancellationToken);
        var delay = checked((ushort)GifEncodingPolicy.FrameDelayHundredths(framesPerSecond));
        long frameCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await frameFactory(frameCount, cancellationToken);
            if (frame is null) break;
            ValidateFrame(frame, width, height);
            var indexes = Quantize(frame.BgraBytes, width, height);
            var compressed = GifEncodingPolicy.EncodeLzw(indexes);
            await WriteFrameAsync(stream, width, height, delay, compressed, cancellationToken);
            frameCount = checked(frameCount + 1);
        }

        if (frameCount == 0) throw new InvalidDataException("GIF recording produced no frames.");
        await stream.WriteAsync(new byte[] { 0x3B }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        if (stream.Length <= 0) throw new InvalidDataException("GIF encoder produced an empty file.");
    }

    private static async Task WriteHeaderAsync(Stream stream, int width, int height, CancellationToken token)
    {
        await stream.WriteAsync("GIF89a"u8.ToArray(), token);
        var descriptor = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(0, 2), checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2, 2), checked((ushort)height));
        descriptor[4] = 0xF7; // global table, 8-bit color resolution, 256 entries
        descriptor[5] = 0;
        descriptor[6] = 0;
        await stream.WriteAsync(descriptor, token);
        await stream.WriteAsync(GifEncodingPolicy.BuildRgb332Palette(), token);
        await stream.WriteAsync(new byte[]
        {
            0x21, 0xFF, 0x0B,
            (byte)'N',(byte)'E',(byte)'T',(byte)'S',(byte)'C',(byte)'A',(byte)'P',(byte)'E',(byte)'2',(byte)'.',(byte)'0',
            0x03, 0x01, 0x00, 0x00, 0x00
        }, token);
    }

    private static async Task WriteFrameAsync(Stream stream, int width, int height, ushort delay, byte[] compressed, CancellationToken token)
    {
        var gce = new byte[] { 0x21, 0xF9, 0x04, 0x00, (byte)(delay & 0xFF), (byte)(delay >> 8), 0x00, 0x00 };
        await stream.WriteAsync(gce, token);
        var descriptor = new byte[10];
        descriptor[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(5, 2), checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(7, 2), checked((ushort)height));
        descriptor[9] = 0;
        await stream.WriteAsync(descriptor, token);
        await stream.WriteAsync(new byte[] { GifEncodingPolicy.MinimumCodeSize }, token);
        for (var offset = 0; offset < compressed.Length; offset += 255)
        {
            var count = Math.Min(255, compressed.Length - offset);
            await stream.WriteAsync(new byte[] { checked((byte)count) }, token);
            await stream.WriteAsync(compressed.AsMemory(offset, count), token);
        }
        await stream.WriteAsync(new byte[] { 0x00 }, token);
    }

    private static byte[] Quantize(byte[] bgra, int width, int height)
    {
        var pixelCount = checked(width * height);
        if (bgra.Length != checked(pixelCount * 4)) throw new InvalidDataException("GIF BGRA frame length is invalid.");
        var indexes = new byte[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var o = i * 4;
            indexes[i] = GifEncodingPolicy.ToPaletteIndex(bgra[o + 2], bgra[o + 1], bgra[o]);
        }
        return indexes;
    }

    private static void ValidateFrame(RecordingFramePixels frame, int width, int height)
    {
        if (frame.Width != width || frame.Height != height)
            throw new InvalidDataException("GIF recording frame dimensions changed during recording.");
        if (frame.BgraBytes.Length != checked(width * height * 4))
            throw new InvalidDataException("GIF recording frame buffer length is invalid.");
    }
}
