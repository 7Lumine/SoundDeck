using NAudio.Wave;

namespace VoicePipe.Audio;

public sealed class DownmixToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _sourceBuffer = Array.Empty<float>();

    public DownmixToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesRequested = count / 2;
        var sourceSamplesRequested = framesRequested * _sourceChannels;
        if (_sourceBuffer.Length < sourceSamplesRequested)
        {
            _sourceBuffer = new float[sourceSamplesRequested];
        }

        var sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesRequested);
        var framesRead = sourceSamplesRead / _sourceChannels;

        for (var frame = 0; frame < framesRead; frame++)
        {
            var sourceOffset = frame * _sourceChannels;
            var left = _sourceBuffer[sourceOffset];
            var right = _sourceChannels > 1 ? _sourceBuffer[sourceOffset + 1] : left;

            buffer[offset + frame * 2] = left;
            buffer[offset + frame * 2 + 1] = right;
        }

        return framesRead * 2;
    }
}
