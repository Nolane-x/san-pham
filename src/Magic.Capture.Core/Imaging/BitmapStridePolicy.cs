namespace Magic.Capture.Core.Imaging;

/// <summary>
/// Computes byte offsets from BitmapData.Scan0. Scan0 addresses the first logical
/// scan line; the signed stride advances to the next logical scan line, including
/// bottom-up bitmaps whose stride is negative.
/// </summary>
public static class BitmapStridePolicy
{
    public static int RowOffset(int row, int stride)
    {
        if (row < 0) throw new ArgumentOutOfRangeException(nameof(row));
        return checked(row * stride);
    }
}
