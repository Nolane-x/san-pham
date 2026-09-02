using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed class GdiCaptureBackend : ICaptureBackend
{
    public CaptureBackendKind Kind => CaptureBackendKind.Gdi;

    public CaptureBackendProbe Probe() => new(Kind, IsAvailable: OperatingSystem.IsWindows(), OperatingSystem.IsWindows() ? null : "GDI screen capture requires Windows.");

    public Task<CaptureBackendFrame> CaptureAsync(CaptureBackendRequest request, CancellationToken cancellationToken)
    {
        var bounds = request.Bounds;
        if (bounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(request));
        ImageWorkloadLimits.ValidateDimensions(bounds.Width, bounds.Height);
        var failures = new List<string>(CaptureRetryPolicy.MaximumAttempts - 1);

        for (var attempt = 1; attempt <= CaptureRetryPolicy.MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
                    if (request.IncludeCursor) DrawCursorIfVisible(graphics, bounds);
                }

                var png = BitmapCodec.EncodePng(bitmap);
                if (!PngDimensions.TryRead(png, out var width, out var height) || width != bounds.Width || height != bounds.Height)
                    throw new InvalidDataException("GDI screen capture returned unexpected physical-pixel dimensions.");

                return Task.FromResult(new CaptureBackendFrame(png, bounds, RecoveryCount: attempt - 1));
            }
            catch (Exception ex) when (IsTransient(ex) && CaptureRetryPolicy.ShouldRetry(attempt, transientFailure: true))
            {
                failures.Add($"{ex.GetType().Name}: {ex.Message}");
                if (cancellationToken.WaitHandle.WaitOne(CaptureRetryPolicy.RetryDelayMilliseconds))
                    cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                failures.Add($"{ex.GetType().Name}: {ex.Message}");
                throw new CaptureBackendException(
                    Kind,
                    CaptureBackendFailureKind.Permanent,
                    $"GDI screen capture failed after {attempt} bounded attempt(s) for physical-pixel region {bounds.X},{bounds.Y} {bounds.Width}×{bounds.Height}.",
                    ex,
                    attempt - 1);
            }
        }

        throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Permanent, "GDI screen capture exhausted its bounded retry budget.");
    }

    private static bool IsTransient(Exception ex) => ex is ExternalException or Win32Exception;

    private static void DrawCursorIfVisible(Graphics graphics, Magic.Capture.Core.Geometry.PixelRect captureBounds)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!NativeMethods.GetCursorInfo(ref info) || (info.Flags & NativeConstants.CursorShowing) == 0 || info.CursorHandle == IntPtr.Zero)
            return;
        if (!NativeMethods.GetIconInfo(info.CursorHandle, out var icon)) return;
        try
        {
            var x = info.ScreenPosition.X - (int)icon.HotspotX;
            var y = info.ScreenPosition.Y - (int)icon.HotspotY;
            if (x >= captureBounds.Right || y >= captureBounds.Bottom || info.ScreenPosition.X < captureBounds.X || info.ScreenPosition.Y < captureBounds.Y)
                return;
            var hdc = graphics.GetHdc();
            try
            {
                NativeMethods.DrawIconEx(hdc, x - captureBounds.X, y - captureBounds.Y, info.CursorHandle, 0, 0, 0, IntPtr.Zero, NativeConstants.DiNormal);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        finally
        {
            if (icon.ColorBitmap != IntPtr.Zero) NativeMethods.DeleteObject(icon.ColorBitmap);
            if (icon.MaskBitmap != IntPtr.Zero) NativeMethods.DeleteObject(icon.MaskBitmap);
        }
    }
}

internal sealed class CaptureBackendException : Exception
{
    public CaptureBackendException(
        CaptureBackendKind backend,
        CaptureBackendFailureKind failureKind,
        string message,
        Exception? innerException = null,
        int recoveryCount = 0)
        : base(message, innerException)
    {
        Backend = backend;
        FailureKind = failureKind;
        RecoveryCount = recoveryCount;
    }

    public CaptureBackendKind Backend { get; }
    public CaptureBackendFailureKind FailureKind { get; }
    public int RecoveryCount { get; }
}
