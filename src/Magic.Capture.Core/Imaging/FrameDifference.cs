namespace Magic.Capture.Core.Imaging;

public static class FrameDifference
{
    public static double SampledChangedPercent(
        ReadOnlySpan<byte> firstBgra,
        ReadOnlySpan<byte> secondBgra,
        int sampleEveryPixels = 8,
        byte channelThreshold = 8)
    {
        if (firstBgra.Length == 0 || firstBgra.Length != secondBgra.Length || (firstBgra.Length & 3) != 0)
            throw new ArgumentException("BGRA buffers must be non-empty, equal length and contain complete pixels.");
        sampleEveryPixels = Math.Clamp(sampleEveryPixels, 1, 1024);
        var pixelCount = firstBgra.Length / 4;
        long sampled = 0;
        long changed = 0;
        for (var pixel = 0; pixel < pixelCount; pixel += sampleEveryPixels)
        {
            var offset = pixel * 4;
            var db = Math.Abs(firstBgra[offset] - secondBgra[offset]);
            var dg = Math.Abs(firstBgra[offset + 1] - secondBgra[offset + 1]);
            var dr = Math.Abs(firstBgra[offset + 2] - secondBgra[offset + 2]);
            if (Math.Max(db, Math.Max(dg, dr)) > channelThreshold) changed++;
            sampled++;
        }
        return sampled == 0 ? 0 : changed * 100d / sampled;
    }
}
