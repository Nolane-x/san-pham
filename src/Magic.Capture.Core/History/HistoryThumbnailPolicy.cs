namespace Magic.Capture.Core.History;

public static class HistoryThumbnailPolicy
{
    // Thumbnail generation through System.Drawing requires a full decoded source bitmap. Keep that
    // path below the interactive working-set budget; larger captures remain fully usable and WinUI
    // decodes the original image directly to the requested preview size on demand.
    public const long MaximumPreGeneratedSourcePixels = 24_000_000;

    public static bool ShouldPreGenerate(int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        return (long)width * height <= MaximumPreGeneratedSourcePixels;
    }
}
