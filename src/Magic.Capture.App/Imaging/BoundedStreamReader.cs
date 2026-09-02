namespace Magic.Capture.App.Imaging;

/// <summary>
/// Reads a stream into exactly one bounded buffer. The expected length must come from trusted
/// container/file metadata that has already been captured while the stream is open.
/// </summary>
internal static class BoundedStreamReader
{
    public static async Task<byte[]> ReadExactAsync(
        Stream stream,
        long expectedLength,
        long maximumLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new InvalidOperationException("The source stream is not readable.");
        if (maximumLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        if (expectedLength <= 0) throw new InvalidDataException("The source payload is empty.");
        if (expectedLength > maximumLength)
            throw new InvalidDataException($"The source payload exceeds the safe {maximumLength / (1024 * 1024):N0} MB limit.");
        if (expectedLength > int.MaxValue)
            throw new InvalidDataException("The source payload is too large to materialize safely in memory.");

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)expectedLength));
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("The source stream ended before the declared length was read.");
            offset += read;
        }
        return bytes;
    }
}
