using Magic.Capture.App.Capture;
using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai;

internal sealed class AiContextStackService
{
    private readonly ContextStack _core = new(8);
    private readonly Dictionary<Guid, CaptureAsset> _assets = [];

    public IReadOnlyList<CaptureAsset> Assets => _core.Items.Select(i => _assets[i.CaptureId]).ToArray();
    public int Count => _core.Items.Count;

    public bool TryAdd(CaptureAsset asset, string? label = null)
    {
        if (!_core.TryAdd(new ContextStackItem(asset.Id, label ?? asset.SourceDisplayName ?? asset.SourceKind.ToString()))) return false;
        _assets[asset.Id] = asset;
        return true;
    }

    public bool Remove(Guid captureId)
    {
        if (!_core.Remove(captureId)) return false;
        _assets.Remove(captureId);
        return true;
    }

    public void Clear()
    {
        _core.Clear();
        _assets.Clear();
    }
}
