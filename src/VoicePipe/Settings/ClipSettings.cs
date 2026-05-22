namespace VoicePipe.Settings;

public sealed class ClipSettings
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public bool IsLoopEnabled { get; set; }
    public bool IsPinned { get; set; }
    public string HotkeyText { get; set; } = string.Empty;
}
