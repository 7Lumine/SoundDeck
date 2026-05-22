using NAudio.Wave;

namespace VoicePipe.Audio;

internal sealed class SoundDeckMixerSampleProvider : ISampleProvider
{
    private const int Channels = 2;

    private readonly VoicePipeEngine _engine;
    private float[] _micBuffer = Array.Empty<float>();
    private float[] _clipMixBuffer = Array.Empty<float>();
    private float[] _clipReadBuffer = Array.Empty<float>();
    private float[] _monitorBuffer = Array.Empty<float>();

    public SoundDeckMixerSampleProvider(VoicePipeEngine engine, int sampleRate)
    {
        _engine = engine;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        var frames = count / Channels;
        EnsureBuffers(frames, count);
        var micSamplesRead = _engine.ReadMicSamples(_micBuffer, 0, frames);
        if (micSamplesRead < frames)
        {
            Array.Clear(_micBuffer, micSamplesRead, frames - micSamplesRead);
        }

        Array.Clear(_clipMixBuffer, 0, count);
        _engine.MixActiveClips(_clipMixBuffer, 0, count, _clipReadBuffer);

        var micTrimGain = VoicePipeEngine.DbToLinear(_engine.MicInputTrimDb);
        var micBaseGain = _engine.MicMuted ? 0.0f : VoicePipeEngine.ClampGain(_engine.MicVolume) * micTrimGain;
        var micMixerGain = micBaseGain;
        if (_engine.DuckingEnabled && _engine.ActiveClipCount > 0)
        {
            micMixerGain *= VoicePipeEngine.DbToLinear(_engine.DuckingAmountDb);
        }

        var mp3Gain = VoicePipeEngine.ClampGain(_engine.Mp3Volume);
        var monitorState = _engine.GetMonitorState();
        var writeMonitor = monitorState.Mode != MonitorMode.None && monitorState.Enabled;
        if (writeMonitor)
        {
            Array.Clear(_monitorBuffer, 0, count);
        }

        var micSumSquares = 0.0;
        var clipsSumSquares = 0.0;
        var outputSumSquares = 0.0;
        var micPeak = 0.0;
        var clipsPeak = 0.0;
        var outputPeak = 0.0;

        for (var frame = 0; frame < frames; frame++)
        {
            var micRaw = _micBuffer[frame];
            var micMetered = micRaw * micBaseGain;
            var micMixed = micRaw * micMixerGain;

            for (var channel = 0; channel < Channels; channel++)
            {
                var sampleIndex = offset + frame * Channels + channel;
                var clipSample = _clipMixBuffer[frame * Channels + channel] * mp3Gain;
                var mixed = VoicePipeEngine.SoftLimit(micMixed + clipSample);
                buffer[sampleIndex] = mixed;

                var monitorSample = monitorState.Mode switch
                {
                    MonitorMode.Mp3Only => VoicePipeEngine.SoftLimit(clipSample),
                    MonitorMode.Mixed => mixed,
                    _ => 0.0f
                };

                if (writeMonitor)
                {
                    _monitorBuffer[frame * Channels + channel] = VoicePipeEngine.SoftLimit(monitorSample * monitorState.Volume);
                }

                micSumSquares += micMetered * micMetered;
                clipsSumSquares += clipSample * clipSample;
                outputSumSquares += mixed * mixed;
                micPeak = Math.Max(micPeak, Math.Abs(micMetered));
                clipsPeak = Math.Max(clipsPeak, Math.Abs(clipSample));
                outputPeak = Math.Max(outputPeak, Math.Abs(mixed));
            }
        }

        if (writeMonitor)
        {
            _engine.WriteMonitorSamples(_monitorBuffer, 0, count);
        }

        var meteredSamples = Math.Max(1, frames * Channels);
        _engine.SetLevelsFromAudioThread(
            VoicePipeEngine.CalculateMeterLevel(micSumSquares, micPeak, meteredSamples),
            VoicePipeEngine.CalculateMeterLevel(clipsSumSquares, clipsPeak, meteredSamples),
            VoicePipeEngine.CalculateMeterLevel(outputSumSquares, outputPeak, meteredSamples));

        return count;
    }

    private void EnsureBuffers(int frames, int sampleCount)
    {
        if (_micBuffer.Length < frames)
        {
            _micBuffer = new float[frames];
        }

        if (_clipMixBuffer.Length < sampleCount)
        {
            _clipMixBuffer = new float[sampleCount];
        }

        if (_clipReadBuffer.Length < sampleCount)
        {
            _clipReadBuffer = new float[sampleCount];
        }

        if (_monitorBuffer.Length < sampleCount)
        {
            _monitorBuffer = new float[sampleCount];
        }
    }
}
