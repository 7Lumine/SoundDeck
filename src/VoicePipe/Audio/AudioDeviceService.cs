using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace VoicePipe.Audio;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        for (var index = 0; index < WaveIn.DeviceCount; index++)
        {
            var caps = WaveIn.GetCapabilities(index);
            devices.Add(new AudioDeviceInfo(index, caps.ProductName));
        }

        return devices;
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices(bool includeDefaultDevice = false)
    {
        var devices = new List<AudioDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();

        if (includeDefaultDevice)
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            devices.Add(new AudioDeviceInfo(-1, "既定の再生デバイス", defaultDevice.ID));
        }

        var endpoints = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .ToList();
        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index];
            devices.Add(new AudioDeviceInfo(index, endpoint.FriendlyName, endpoint.ID));
        }

        return devices;
    }
}
