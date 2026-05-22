using NAudio.Wave;

namespace VoicePipe.Audio;

internal sealed class DiagnosticSignalSampleProvider : ISampleProvider
{
    private readonly bool _tone;
    private readonly float _amplitude;
    private readonly double _phaseIncrement;
    private double _phase;

    public DiagnosticSignalSampleProvider(bool tone, int sampleRate, int channels)
    {
        _tone = tone;
        _amplitude = 0.25f;
        _phaseIncrement = 2.0 * Math.PI * 440.0 / sampleRate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_tone)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        for (var index = 0; index < count; index += WaveFormat.Channels)
        {
            var sample = MathF.Sin((float)_phase) * _amplitude;
            _phase += _phaseIncrement;
            if (_phase > Math.PI * 2)
            {
                _phase -= Math.PI * 2;
            }

            for (var channel = 0; channel < WaveFormat.Channels && index + channel < count; channel++)
            {
                buffer[offset + index + channel] = sample;
            }
        }

        return count;
    }
}
