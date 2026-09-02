using System.Runtime.InteropServices;
using Magic.Capture.Core.Capture;
using SharpGen.Runtime;
using Vortice.DXGI;

namespace Magic.Capture.App.Capture;

internal static class CaptureBackendFailureClassifier
{
    private const int EAccessDenied = unchecked((int)0x80070005);

    public static CaptureBackendFailureKind FromHResult(int hresult)
    {
        if (hresult == EAccessDenied) return CaptureBackendFailureKind.AccessDenied;
        if (hresult == ResultCode.AccessLost.Code) return CaptureBackendFailureKind.AccessLost;
        if (hresult == ResultCode.DeviceRemoved.Code) return CaptureBackendFailureKind.DeviceRemoved;
        if (hresult == ResultCode.DeviceReset.Code) return CaptureBackendFailureKind.DeviceReset;
        if (hresult == ResultCode.WaitTimeout.Code) return CaptureBackendFailureKind.Timeout;
        return CaptureBackendFailureKind.Permanent;
    }

    public static CaptureBackendFailureKind FromException(Exception ex) => ex switch
    {
        OperationCanceledException => CaptureBackendFailureKind.Cancelled,
        SharpGenException sharp => FromHResult(sharp.ResultCode.Code),
        COMException com => FromHResult(com.HResult),
        _ => FromHResult(ex.HResult)
    };
}
