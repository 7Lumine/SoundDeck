using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoicePipe.Audio;

public sealed class DownmixToMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _sourceBuffer = Array.Empty<float>();

    public DownmixToMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sourceChannels == 1)
        {
            return _source.Read(buffer, offset, count);
        }

        var sourceSamplesRequested = count * _sourceChannels;
        if (_sourceBuffer.Length < sourceSamplesRequested)
        {
            _sourceBuffer = new float[sourceSamplesRequested];
        }

        var sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesRequested);
        var framesRead = sourceSamplesRead / _sourceChannels;

        for (var frame = 0; frame < framesRead; frame++)
        {
            var sum = 0.0f;
            var sourceOffset = frame * _sourceChannels;

            for (var channel = 0; channel < _sourceChannels; channel++)
            {
                sum += _sourceBuffer[sourceOffset + channel];
            }

            buffer[offset + frame] = sum / _sourceChannels;
        }

        return framesRead;
    }
}
