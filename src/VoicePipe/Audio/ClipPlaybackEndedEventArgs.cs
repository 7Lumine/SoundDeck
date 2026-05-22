namespace VoicePipe.Audio;

public sealed class ClipPlaybackEndedEventArgs : EventArgs
{
    public ClipPlaybackEndedEventArgs(Guid playbackId, bool requestedStop)
    {
        PlaybackId = playbackId;
        RequestedStop = requestedStop;
    }

    public Guid PlaybackId { get; }
    public bool RequestedStop { get; }
}
