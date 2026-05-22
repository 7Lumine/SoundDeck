using NAudio.Wave;

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
        if (includeDefaultDevice)
        {
            devices.Add(new AudioDeviceInfo(-1, "既定の再生デバイス"));
        }

        for (var index = 0; index < WaveOut.DeviceCount; index++)
        {
            var caps = WaveOut.GetCapabilities(index);
            devices.Add(new AudioDeviceInfo(index, caps.ProductName));
        }

        return devices;
    }
}
