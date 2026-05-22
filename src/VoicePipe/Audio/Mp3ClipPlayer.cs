using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoicePipe.Audio;

public sealed class Mp3ClipPlayer : IDisposable
{
    private readonly object _gate = new();
    private AudioFileReader? _reader;
    private ISampleProvider? _provider;

    public bool IsPlaying { get; private set; }
    public bool Loop { get; private set; }
    public int SampleRate { get; private set; } = 48_000;
    public int OriginalSampleRate { get; private set; }
    public int OriginalChannels { get; private set; }
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _reader?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return _reader?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    public void Load(string path, int targetSampleRate, bool loop)
    {
        lock (_gate)
        {
            DisposeReader();

            _reader = new AudioFileReader(path);
            OriginalSampleRate = _reader.WaveFormat.SampleRate;
            OriginalChannels = _reader.WaveFormat.Channels;
            _provider = CreateProvider(_reader, targetSampleRate);
            SampleRate = targetSampleRate;
            Loop = loop;
            IsPlaying = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            DisposeReader();
            IsPlaying = false;
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_reader is null)
            {
                return;
            }

            var duration = _reader.TotalTime;
            if (duration > TimeSpan.Zero)
            {
                position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
                position = position > duration ? duration : position;
            }

            _reader.CurrentTime = position;
            _provider = CreateProvider(_reader, SampleRate);
            IsPlaying = true;
        }
    }

    public void SetLoop(bool loop)
    {
        lock (_gate)
        {
            Loop = loop;
        }
    }

    public void Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        lock (_gate)
        {
            if (!IsPlaying || _reader is null || _provider is null)
            {
                return;
            }

            var totalRead = 0;
            while (totalRead < count)
            {
                var read = _provider.Read(buffer, offset + totalRead, count - totalRead);
                totalRead += read;

                if (read > 0)
                {
                    continue;
                }

                if (!Loop)
                {
                    IsPlaying = false;
                    break;
                }

                _reader.Position = 0;
                _provider = CreateProvider(_reader, SampleRate);
            }
        }
    }

    private static ISampleProvider CreateProvider(AudioFileReader reader, int targetSampleRate)
    {
        ISampleProvider provider = reader;

        if (provider.WaveFormat.SampleRate != targetSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, targetSampleRate);
        }

        provider = provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => new DownmixToStereoSampleProvider(provider)
        };

        return provider;
    }

    private void DisposeReader()
    {
        _provider = null;
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeReader();
        }
    }
}
