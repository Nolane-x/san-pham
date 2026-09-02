namespace Magic.Capture.App.Recording;

internal sealed class BoundedPcmBuffer
{
    private readonly byte[] _buffer;
    private readonly object _gate = new();
    private int _read;
    private int _write;
    private int _count;
    private long _droppedBytes;

    public BoundedPcmBuffer(int capacityBytes)
    {
        if (capacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBytes));
        _buffer = new byte[capacityBytes];
    }

    public int Capacity => _buffer.Length;
    public int Count { get { lock (_gate) return _count; } }
    public long DroppedBytes { get { lock (_gate) return _droppedBytes; } }

    public void Write(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return;
        lock (_gate)
        {
            if (source.Length >= _buffer.Length)
            {
                var discarded = checked(_count + source.Length - _buffer.Length);
                source = source[^_buffer.Length..];
                _read = 0;
                _write = 0;
                _count = 0;
                _droppedBytes = checked(_droppedBytes + discarded);
            }
            else
            {
                var overflow = Math.Max(0, _count + source.Length - _buffer.Length);
                if (overflow > 0)
                {
                    AdvanceRead(overflow);
                    _droppedBytes = checked(_droppedBytes + overflow);
                }
            }

            var first = Math.Min(source.Length, _buffer.Length - _write);
            source[..first].CopyTo(_buffer.AsSpan(_write, first));
            var remaining = source.Length - first;
            if (remaining > 0) source[first..].CopyTo(_buffer.AsSpan(0, remaining));
            _write = (_write + source.Length) % _buffer.Length;
            _count += source.Length;
        }
    }

    public int ReadAndFillSilence(Span<byte> destination)
    {
        destination.Clear();
        if (destination.IsEmpty) return 0;
        lock (_gate)
        {
            var copy = Math.Min(destination.Length, _count);
            var first = Math.Min(copy, _buffer.Length - _read);
            _buffer.AsSpan(_read, first).CopyTo(destination[..first]);
            var remaining = copy - first;
            if (remaining > 0) _buffer.AsSpan(0, remaining).CopyTo(destination.Slice(first, remaining));
            AdvanceRead(copy);
            return copy;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _read = 0;
            _write = 0;
            _count = 0;
        }
    }

    private void AdvanceRead(int bytes)
    {
        if (bytes <= 0) return;
        if (bytes > _count) throw new ArgumentOutOfRangeException(nameof(bytes));
        _read = (_read + bytes) % _buffer.Length;
        _count -= bytes;
    }
}
