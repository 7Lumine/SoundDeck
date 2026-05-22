namespace VoicePipe.Audio;

internal sealed class ActiveClipPlayback : IDisposable
{
    public ActiveClipPlayback(Guid id, string path, Mp3ClipPlayer player, float volume)
    {
        Id = id;
        Path = path;
        Player = player;
        Volume = volume;
    }

    public Guid Id { get; }
    public string Path { get; }
    public Mp3ClipPlayer Player { get; }
    public float Volume { get; set; }
    public bool IsPlaying => Player.IsPlaying;

    public void Dispose()
    {
        Player.Dispose();
    }
}
