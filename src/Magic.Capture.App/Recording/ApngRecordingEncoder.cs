using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal sealed class ApngRecordingEncoder
{
    private const int ChunkPayloadBytes = 64 * 1024;
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public async Task EncodeAsync(
        string path,
        int width,
        int height,
        int framesPerSecond,
        Func<long, CancellationToken, Task<RecordingFramePixels?>> frameFactory,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("APNG output path must be fully qualified.", nameof(path));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(frameFactory);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        await stream.WriteAsync(Signature, cancellationToken);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6; // RGBA
        await WriteChunkAsync(stream, "IHDR", ihdr, cancellationToken);

        var animationControlStart = stream.Position;
        await WriteChunkAsync(stream, "acTL", new byte[8], cancellationToken); // patched after final frame count is known
        var delay = ApngEncodingPolicy.FrameDelay(framesPerSecond);
        uint sequence = 0;
        uint frameCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await frameFactory(frameCount, cancellationToken);
            if (frame is null) break;
            ValidateFrame(frame, width, height);
            var control = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(0, 4), sequence++);
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4, 4), checked((uint)width));
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(8, 4), checked((uint)height));
            BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20, 2), delay.Numerator);
            BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22, 2), delay.Denominator);
            control[24] = 0; // APNG_DISPOSE_OP_NONE
            control[25] = 0; // APNG_BLEND_OP_SOURCE
            await WriteChunkAsync(stream, "fcTL", control, cancellationToken);

            var compressed = CompressRgba(frame.BgraBytes, width, height);
            if (frameCount == 0)
            {
                for (var offset = 0; offset < compressed.Length; offset += ChunkPayloadBytes)
                {
                    var count = Math.Min(ChunkPayloadBytes, compressed.Length - offset);
                    await WriteChunkAsync(stream, "IDAT", compressed.AsMemory(offset, count), cancellationToken);
                }
            }
            else
            {
                for (var offset = 0; offset < compressed.Length; offset += ChunkPayloadBytes)
                {
                    var count = Math.Min(ChunkPayloadBytes, compressed.Length - offset);
                    var payload = new byte[checked(count + 4)];
                    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), sequence++);
                    compressed.AsSpan(offset, count).CopyTo(payload.AsSpan(4));
                    await WriteChunkAsync(stream, "fdAT", payload, cancellationToken);
                }
            }
            frameCount = checked(frameCount + 1);
        }

        if (frameCount == 0) throw new InvalidDataException("APNG recording produced no frames.");
        await WriteChunkAsync(stream, "IEND", ReadOnlyMemory<byte>.Empty, cancellationToken);
        await PatchAnimationControlAsync(stream, animationControlStart, frameCount, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        if (stream.Length <= Signature.Length) throw new InvalidDataException("APNG encoder produced an empty file.");
    }

    private static byte[] CompressRgba(byte[] bgra, int width, int height)
    {
        if (bgra.Length != checked(width * height * 4)) throw new InvalidDataException("APNG BGRA frame length is invalid.");
        using var output = new MemoryStream(Math.Min(bgra.Length, 4 * 1024 * 1024));
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 4 + 1)];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0;
                for (var x = 0; x < width; x++)
                {
                    var src = checked((y * width + x) * 4);
                    var dst = 1 + x * 4;
                    row[dst] = bgra[src + 2];
                    row[dst + 1] = bgra[src + 1];
                    row[dst + 2] = bgra[src];
                    row[dst + 3] = bgra[src + 3];
                }
                zlib.Write(row, 0, row.Length);
            }
        }
        return output.ToArray();
    }

    private static async Task PatchAnimationControlAsync(FileStream stream, long chunkStart, uint frameCount, CancellationToken token)
    {
        var data = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 0); // infinite loop
        var returnPosition = stream.Position;
        stream.Position = checked(chunkStart + 8);
        await stream.WriteAsync(data, token);
        var crcInput = new byte[12];
        "acTL"u8.CopyTo(crcInput.AsSpan(0, 4));
        data.CopyTo(crcInput, 4);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ApngEncodingPolicy.Crc32(crcInput));
        stream.Position = checked(chunkStart + 16);
        await stream.WriteAsync(crc, token);
        stream.Position = returnPosition;
    }

    private static async Task WriteChunkAsync(Stream stream, string type, ReadOnlyMemory<byte> data, CancellationToken token)
    {
        if (type.Length != 4) throw new ArgumentException("PNG chunk type must be four ASCII characters.", nameof(type));
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        await stream.WriteAsync(length, token);
        await stream.WriteAsync(typeBytes, token);
        if (!data.IsEmpty) await stream.WriteAsync(data, token);
        var crcInput = new byte[checked(4 + data.Length)];
        typeBytes.CopyTo(crcInput, 0);
        data.Span.CopyTo(crcInput.AsSpan(4));
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ApngEncodingPolicy.Crc32(crcInput));
        await stream.WriteAsync(crcBytes, token);
    }

    private static void ValidateFrame(RecordingFramePixels frame, int width, int height)
    {
        if (frame.Width != width || frame.Height != height)
            throw new InvalidDataException("APNG recording frame dimensions changed during recording.");
        if (frame.BgraBytes.Length != checked(width * height * 4))
            throw new InvalidDataException("APNG recording frame buffer length is invalid.");
    }
}
