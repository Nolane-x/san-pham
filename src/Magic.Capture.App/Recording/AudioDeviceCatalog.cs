using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingAudioDevice(string Id, string Name, bool IsDefault, bool IsRender);

internal sealed class AudioDeviceCatalog
{
    public IReadOnlyList<RecordingAudioDevice> GetRenderDevices() => Enumerate(DataFlow.Render);
    public IReadOnlyList<RecordingAudioDevice> GetCaptureDevices() => Enumerate(DataFlow.Capture);

    private static IReadOnlyList<RecordingAudioDevice> Enumerate(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            defaultId = defaultDevice.ID;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            defaultId = null;
        }

        var result = new List<RecordingAudioDevice>();
        using var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var device in devices)
        {
            using (device)
            {
                var id = device.ID;
                var name = string.IsNullOrWhiteSpace(device.FriendlyName) ? "Windows audio device" : device.FriendlyName.Trim();
                result.Add(new RecordingAudioDevice(
                    id,
                    name,
                    string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                    flow == DataFlow.Render));
            }
        }
        return result
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
