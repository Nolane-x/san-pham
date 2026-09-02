namespace Magic.Capture.Core.Ai;

public sealed record ContextStackItem(Guid CaptureId, string Label);

public sealed class ContextStack
{
    private readonly List<ContextStackItem> _items = [];
    public ContextStack(int maxItems = 8) { if (maxItems is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maxItems)); MaxItems = maxItems; }
    public int MaxItems { get; }
    public IReadOnlyList<ContextStackItem> Items => _items;

    public bool TryAdd(ContextStackItem item)
    {
        if (_items.Count >= MaxItems || _items.Any(x => x.CaptureId == item.CaptureId)) return false;
        _items.Add(item);
        return true;
    }

    public bool Remove(Guid captureId)
    {
        var index = _items.FindIndex(x => x.CaptureId == captureId);
        if (index < 0) return false;
        _items.RemoveAt(index);
        return true;
    }

    public void Clear() => _items.Clear();
}
