using System.Drawing;
using System.Drawing.Imaging;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace Magic.Capture.App.Capture;

/// <summary>DXGI Desktop Duplication backend for a single physical output.</summary>
internal sealed class DesktopDuplicationCaptureBackend : ICaptureBackend
{
    private const uint AcquireTimeoutMilliseconds = 1000;
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    public CaptureBackendKind Kind => CaptureBackendKind.DesktopDuplication;

    public CaptureBackendProbe Probe()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            return new(Kind, false, "DXGI Desktop Duplication requires Windows 8 or later.");
        try
        {
            using var factory = CreateDXGIFactory1<IDXGIFactory1>();
            return new(Kind, true);
        }
        catch (Exception ex)
        {
            return new(Kind, false, $"Desktop Duplication probe failed: {ex.Message}");
        }
    }

    public Task<CaptureBackendFrame> CaptureAsync(CaptureBackendRequest request, CancellationToken cancellationToken)
    {
        if (request.IncludeCursor)
            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Unsupported,
                "Desktop Duplication pointer-shape composition is not enabled in this release.");
        if (request.MonitorHandle == IntPtr.Zero)
            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Unsupported,
                "Desktop Duplication requires a concrete HMONITOR.");

        for (var rebuild = 0; rebuild <= CaptureBackendRecoveryPolicy.DesktopDuplicationRebuildBudget; rebuild++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var frame = CaptureOnce(request, cancellationToken);
                return Task.FromResult(frame with { RecoveryCount = rebuild });
            }
            catch (CaptureBackendException ex)
            {
                if (CaptureBackendRecoveryPolicy.ShouldRebuildDesktopDuplication(ex.FailureKind, rebuild))
                {
                    // Every CaptureOnce owns a fresh factory/device/duplication interface. A retry is therefore a full rebuild.
                    continue;
                }

                throw new CaptureBackendException(Kind, ex.FailureKind, ex.Message, ex, rebuild);
            }
            catch (Exception ex)
            {
                var failure = CaptureBackendFailureClassifier.FromException(ex);
                if (CaptureBackendRecoveryPolicy.ShouldRebuildDesktopDuplication(failure, rebuild))
                    continue;
                throw new CaptureBackendException(Kind, failure, "Desktop Duplication setup failed.", ex, rebuild);
            }
        }

        throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Permanent,
            "Desktop Duplication exhausted its single interface-rebuild budget.");
    }

    private CaptureBackendFrame CaptureOnce(CaptureBackendRequest request, CancellationToken cancellationToken)
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success; adapterIndex++)
        {
            if (adapter is null) continue;
            using (adapter)
            {
                for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out IDXGIOutput? output).Success; outputIndex++)
                {
                    if (output is null) continue;
                    using (output)
                    {
                        var outputDescription = output.Description;
                        if (outputDescription.Monitor != request.MonitorHandle) continue;
                        return CaptureOutput(adapter, output, outputDescription, request, cancellationToken);
                    }
                }
            }
        }

        throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Unsupported,
            "No DXGI output matched the requested HMONITOR.");
    }

    private CaptureBackendFrame CaptureOutput(
        IDXGIAdapter1 adapter,
        IDXGIOutput output,
        OutputDescription outputDescription,
        CaptureBackendRequest request,
        CancellationToken cancellationToken)
    {
        var desktop = outputDescription.DesktopCoordinates;
        var outputBounds = new PixelRect(desktop.Left, desktop.Top, desktop.Right - desktop.Left, desktop.Bottom - desktop.Top);
        ImageWorkloadLimits.ValidateDimensions(outputBounds.Width, outputBounds.Height);

        D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            FeatureLevels,
            out ID3D11Device device,
            out ID3D11DeviceContext context).CheckError();
        using (device)
        using (context)
        using (var output1 = output.QueryInterface<IDXGIOutput1>())
        using (var duplication = output1.DuplicateOutput(device))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquire = duplication.AcquireNextFrame(AcquireTimeoutMilliseconds, out OutduplFrameInfo frameInfo, out IDXGIResource? resource);
            if (acquire == Vortice.DXGI.ResultCode.WaitTimeout)
                throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Timeout,
                    $"Desktop Duplication did not produce a frame within {AcquireTimeoutMilliseconds} ms.");
            if (acquire.Failure)
                throw new CaptureBackendException(Kind, CaptureBackendFailureClassifier.FromHResult(acquire.Code),
                    $"Desktop Duplication AcquireNextFrame failed with {acquire}.");
            if (resource is null)
            {
                duplication.ReleaseFrame();
                throw new CaptureBackendException(Kind, CaptureBackendFailureKind.InvalidFrame,
                    "Desktop Duplication acquired a frame without a desktop resource.");
            }

            if (!DesktopDuplicationCursorPolicy.CanGuaranteeCursorExcluded(
                    frameInfo.LastMouseUpdateTime,
                    frameInfo.PointerPosition.Visible))
            {
                resource.Dispose();
                duplication.ReleaseFrame();
                throw new CaptureBackendException(Kind, CaptureBackendFailureKind.Unsupported,
                    "DXGI could not prove that the desktop frame excludes the cursor; falling back preserves the capture cursor contract.");
            }

            try
            {
                using (resource)
                using (var sourceTexture = resource.QueryInterface<ID3D11Texture2D>())
                {
                    var sourceDescription = sourceTexture.Description;
                    ImageWorkloadLimits.ValidateDimensions((int)sourceDescription.Width, (int)sourceDescription.Height);
                    using var staging = device.CreateTexture2D(
                        sourceDescription.Format,
                        sourceDescription.Width,
                        sourceDescription.Height,
                        arraySize: 1,
                        mipLevels: 1,
                        bindFlags: BindFlags.None,
                        miscFlags: ResourceOptionFlags.None,
                        usage: ResourceUsage.Staging,
                        cpuAccessFlags: CpuAccessFlags.Read);
                    context.CopyResource(staging, sourceTexture);
                    context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
                    try
                    {
                        using var bitmap = CopyMappedTexture(mapped, checked((int)sourceDescription.Width), checked((int)sourceDescription.Height));
                        ApplyOutputRotation(bitmap, (int)outputDescription.Rotation);
                        if (bitmap.Width != outputBounds.Width || bitmap.Height != outputBounds.Height)
                            throw new CaptureBackendException(Kind, CaptureBackendFailureKind.InvalidFrame,
                                $"Desktop Duplication produced {bitmap.Width}×{bitmap.Height}; output topology expected {outputBounds.Width}×{outputBounds.Height}.");
                        cancellationToken.ThrowIfCancellationRequested();
                        return new CaptureBackendFrame(BitmapCodec.EncodePng(bitmap), outputBounds);
                    }
                    finally
                    {
                        context.Unmap(staging, 0);
                    }
                }
            }
            catch (CaptureBackendException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CaptureBackendException(Kind, CaptureBackendFailureClassifier.FromException(ex),
                    "Desktop Duplication frame readback failed.", ex);
            }
            finally
            {
                duplication.ReleaseFrame();
            }
        }
    }

    private static unsafe Bitmap CopyMappedTexture(MappedSubresource mapped, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked(width * 4);
            for (var y = 0; y < height; y++)
            {
                var source = (byte*)mapped.DataPointer + checked(y * (int)mapped.RowPitch);
                var destination = (byte*)bits.Scan0 + checked(y * bits.Stride);
                Buffer.MemoryCopy(source, destination, Math.Abs(bits.Stride), rowBytes);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
    }

    private static void ApplyOutputRotation(Bitmap bitmap, int rotation)
    {
        // DXGI_MODE_ROTATION: 0 unspecified, 1 identity, 2 rotate90, 3 rotate180, 4 rotate270.
        switch (rotation)
        {
            case 0:
            case 1:
                break;
            case 2:
                bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
                break;
            case 3:
                bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
                break;
            case 4:
                bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone);
                break;
            default:
                throw new InvalidDataException($"Unsupported DXGI output rotation value: {rotation}.");
        }
    }
}
