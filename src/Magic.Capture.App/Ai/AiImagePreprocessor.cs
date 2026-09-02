using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai;

internal sealed class AiImagePreprocessor
{
    private readonly ImageTransformService _transforms;
    public AiImagePreprocessor(ImageTransformService transforms) => _transforms = transforms;

    public byte[] Prepare(CaptureAsset asset, AiModelProfile model)
    {
        var maxEdge = model.VisionQuality switch
        {
            AiVisionQuality.Basic => 1600,
            AiVisionQuality.Strong => 2560,
            _ => 0
        };
        if (maxEdge <= 0 || Math.Max(asset.Width, asset.Height) <= maxEdge) return asset.PngBytes;
        var scale = maxEdge / (double)Math.Max(asset.Width, asset.Height);
        return _transforms.Resize(asset.PngBytes, Math.Max(1, (int)Math.Round(asset.Width * scale)), Math.Max(1, (int)Math.Round(asset.Height * scale)));
    }
}
