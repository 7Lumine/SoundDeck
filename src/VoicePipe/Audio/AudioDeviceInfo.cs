namespace VoicePipe.Audio;

public sealed record AudioDeviceInfo(int DeviceNumber, string Name)
{
    public override string ToString() => Name;
}
