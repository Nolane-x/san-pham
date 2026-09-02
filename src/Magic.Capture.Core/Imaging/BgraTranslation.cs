namespace Magic.Capture.Core.Imaging;

/// <summary>
/// Applies a clipped integer translation to a tightly packed BGRA canvas without allocating a
/// second full-frame buffer. Newly uncovered pixels become transparent black.
/// </summary>
public static class BgraTranslation
{
    public static void TranslateInPlace(byte[] pixels, int width, int height, int offsetX, int offsetY)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var expected = checked(width * height * 4);
        if (pixels.Length != expected) throw new ArgumentException("BGRA buffer length does not match width × height × 4.", nameof(pixels));
        if (offsetX == 0 && offsetY == 0) return;
        if (Math.Abs((long)offsetX) >= width || Math.Abs((long)offsetY) >= height)
        {
            Array.Clear(pixels, 0, pixels.Length);
            return;
        }

        var sourceX = Math.Max(0, -offsetX);
        var destinationX = Math.Max(0, offsetX);
        var copyWidth = width - Math.Abs(offsetX);
        var copyBytes = checked(copyWidth * 4);
        var rowBytes = checked(width * 4);

        if (offsetY > 0)
        {
            for (var sourceY = height - offsetY - 1; sourceY >= 0; sourceY--)
                MoveRow(sourceY, sourceY + offsetY);
            Array.Clear(pixels, 0, checked(offsetY * rowBytes));
        }
        else
        {
            var firstSourceY = -offsetY;
            for (var sourceY = firstSourceY; sourceY < height; sourceY++)
                MoveRow(sourceY, sourceY + offsetY);
            var clearStart = checked((height + offsetY) * rowBytes);
            Array.Clear(pixels, clearStart, checked(-offsetY * rowBytes));
        }

        void MoveRow(int sourceY, int destinationY)
        {
            var sourceIndex = checked(sourceY * rowBytes + sourceX * 4);
            var destinationRowStart = checked(destinationY * rowBytes);
            var destinationIndex = checked(destinationRowStart + destinationX * 4);
            Array.Copy(pixels, sourceIndex, pixels, destinationIndex, copyBytes);
            if (destinationX > 0)
                Array.Clear(pixels, destinationRowStart, destinationX * 4);
            var rightStart = checked(destinationIndex + copyBytes);
            var rightBytes = checked(rowBytes - (rightStart - destinationRowStart));
            if (rightBytes > 0)
                Array.Clear(pixels, rightStart, rightBytes);
        }
    }
}
