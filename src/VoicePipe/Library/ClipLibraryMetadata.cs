namespace VoicePipe.Library;

public sealed class ClipLibraryMetadata
{
    public string DisplayName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public bool IsLoopEnabled { get; set; }
    public bool IsPinned { get; set; }
    public string HotkeyText { get; set; } = string.Empty;
}
