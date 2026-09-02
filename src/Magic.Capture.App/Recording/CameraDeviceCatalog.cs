using Windows.Devices.Enumeration;

namespace Magic.Capture.App.Recording;

internal sealed record CameraDeviceInfo(string Id, string Name);

internal static class CameraDeviceCatalog
{
    public static async Task<IReadOnlyList<CameraDeviceInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        cancellationToken.ThrowIfCancellationRequested();
        return devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .Select(device => new CameraDeviceInfo(device.Id, string.IsNullOrWhiteSpace(device.Name) ? "Camera" : device.Name.Trim()))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
