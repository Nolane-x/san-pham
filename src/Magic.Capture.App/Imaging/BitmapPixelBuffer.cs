using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Imaging;

internal static class BitmapPixelBuffer
{
    public static byte[] ReadBgra(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(bitmap.Width, bitmap.Height);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked(bitmap.Width * 4);
            var result = new byte[checked(rowBytes * bitmap.Height)];
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(IntPtr.Add(data.Scan0, BitmapStridePolicy.RowOffset(row, data.Stride)), result, row * rowBytes, rowBytes);
            return result;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }


    public static byte[] ReadBgraCanvas(
        Bitmap bitmap,
        int canvasWidth,
        int canvasHeight,
        int offsetX = 0,
        int offsetY = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ImageWorkloadLimits.ValidateCompareDimensions(canvasWidth, canvasHeight);
        var rowBytes = checked(canvasWidth * 4);
        var result = new byte[checked(rowBytes * canvasHeight)];

        var sourceX = Math.Max(0, -offsetX);
        var sourceY = Math.Max(0, -offsetY);
        var destinationX = Math.Max(0, offsetX);
        var destinationY = Math.Max(0, offsetY);
        var copyWidth = Math.Min(bitmap.Width - sourceX, canvasWidth - destinationX);
        var copyHeight = Math.Min(bitmap.Height - sourceY, canvasHeight - destinationY);
        if (copyWidth <= 0 || copyHeight <= 0) return result;

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var copyBytes = checked(copyWidth * 4);
            for (var row = 0; row < copyHeight; row++)
            {
                if ((row & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                var sourceRow = sourceY + row;
                var sourcePtr = IntPtr.Add(data.Scan0, checked(BitmapStridePolicy.RowOffset(sourceRow, data.Stride) + sourceX * 4));
                var destinationOffset = checked((destinationY + row) * rowBytes + destinationX * 4);
                Marshal.Copy(sourcePtr, result, destinationOffset, copyBytes);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static void WriteBgra(Bitmap bitmap, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(bytes);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(bitmap.Width, bitmap.Height);
        var rowBytes = checked(bitmap.Width * 4);
        if (bytes.Length != checked(rowBytes * bitmap.Height))
            throw new ArgumentException("Pixel buffer length does not match the bitmap.", nameof(bytes));
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(bytes, row * rowBytes, IntPtr.Add(data.Scan0, BitmapStridePolicy.RowOffset(row, data.Stride)), rowBytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
