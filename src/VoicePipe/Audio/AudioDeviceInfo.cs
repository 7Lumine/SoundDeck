namespace VoicePipe.Audio;

public sealed record AudioDeviceInfo(int DeviceNumber, string Name, string? DeviceId = null)
{
    public override string ToString() => Name;
}
