using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Magic.Capture.App.Capture;

/// <summary>Owns the BGRA-capable hardware D3D11 device used by Windows Graphics Capture.</summary>
internal sealed class Direct3D11DeviceHost : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private readonly object _gate = new();
    private ID3D11Device? _nativeDevice;
    private IDirect3DDevice? _winRtDevice;
    private bool _disposed;

    public IDirect3DDevice GetWinRtDevice()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_winRtDevice is not null) return _winRtDevice;

            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                FeatureLevels,
                out ID3D11Device? device).CheckError();
            if (device is null) throw new InvalidOperationException("D3D11 returned no capture device.");

            try
            {
                using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
                var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    _winRtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable)
                        ?? throw new InvalidOperationException("Could not project the D3D11 device into WinRT.");
                }
                finally
                {
                    if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
                }
                _nativeDevice = device;
                device = null;
                return _winRtDevice;
            }
            finally
            {
                device?.Dispose();
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            (_winRtDevice as IDisposable)?.Dispose();
            _winRtDevice = null;
            _nativeDevice?.Dispose();
            _nativeDevice = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            (_winRtDevice as IDisposable)?.Dispose();
            _winRtDevice = null;
            _nativeDevice?.Dispose();
            _nativeDevice = null;
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
