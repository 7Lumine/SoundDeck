namespace VoicePipe.Settings;

public sealed class UserSettings
{
    public DeviceSelectionSettings Microphone { get; set; } = new();
    public DeviceSelectionSettings VcOutput { get; set; } = new();
    public double MicrophoneVolume { get; set; } = 100;
    public double Mp3Volume { get; set; } = 75;
    public bool MuteMicrophone { get; set; }
    public bool DuckingEnabled { get; set; }
    public double DuckingAmountDb { get; set; } = -12;
    public bool LoopMp3 { get; set; }
    public string MonitorMode { get; set; } = "None";
    public DeviceSelectionSettings MonitorOutput { get; set; } = new() { DeviceNumber = -1, Name = "既定の再生デバイス" };
    public double MonitorVolume { get; set; } = 50;
    public double MicMeterSensitivity { get; set; } = 1.0;
    public double ClipsMeterSensitivity { get; set; } = 1.0;
    public double OutputMeterSensitivity { get; set; } = 1.0;
    public int VcOutputLatencyMilliseconds { get; set; } = 200;
    public bool LowLatencyMode { get; set; }
    public List<ClipSettings> Clips { get; set; } = new();
}
