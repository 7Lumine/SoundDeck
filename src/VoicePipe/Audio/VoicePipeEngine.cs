using NAudio.Wave;

namespace VoicePipe.Audio;

public sealed class VoicePipeEngine : IDisposable
{
    private const int SampleRate = 48_000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int BufferMilliseconds = 20;

    private readonly WaveFormat _format = new(SampleRate, BitsPerSample, Channels);
    private readonly object _clipsGate = new();
    private readonly List<ActiveClipPlayback> _activeClips = new();
    private readonly object _monitorGate = new();
    private WaveInEvent? _microphone;
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _outputBuffer;
    private WaveOutEvent? _monitorOutput;
    private BufferedWaveProvider? _monitorBuffer;
    private float[] _mp3Buffer = Array.Empty<float>();
    private float[] _clipReadBuffer = Array.Empty<float>();
    private byte[] _mixBuffer = Array.Empty<byte>();
    private byte[] _monitorMixBuffer = Array.Empty<byte>();
    private readonly object _levelGate = new();
    private AudioLevels _levels;
    private MonitorMode _monitorMode;
    private int _monitorDeviceNumber = -1;
    private float _monitorVolume = 0.5f;

    public float MicVolume { get; set; } = 1.0f;
    public float Mp3Volume { get; set; } = 0.75f;
    public float MicInputTrimDb { get; set; }
    public float DuckingAmountDb { get; set; } = -12.0f;
    public bool MicMuted { get; set; }
    public bool DuckingEnabled { get; set; }
    public bool IsRunning => _microphone is not null && _output is not null;
    public event EventHandler<ClipPlaybackEndedEventArgs>? ClipPlaybackEnded;
    public int ActiveClipCount
    {
        get
        {
            lock (_clipsGate)
            {
                return _activeClips.Count(clip => clip.IsPlaying);
            }
        }
    }

    public void Start(int inputDeviceNumber, int outputDeviceNumber)
    {
        Stop();

        _outputBuffer = new BufferedWaveProvider(_format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };

        _output = new WaveOutEvent
        {
            DeviceNumber = outputDeviceNumber,
            DesiredLatency = 80
        };
        _output.Init(_outputBuffer);

        _microphone = new WaveInEvent
        {
            DeviceNumber = inputDeviceNumber,
            WaveFormat = _format,
            BufferMilliseconds = BufferMilliseconds,
            NumberOfBuffers = 3
        };
        _microphone.DataAvailable += Microphone_DataAvailable;
        _microphone.RecordingStopped += Microphone_RecordingStopped;

        _output.Play();
        StartMonitorIfNeeded();
        _microphone.StartRecording();
    }

    public void Stop()
    {
        if (_microphone is not null)
        {
            _microphone.DataAvailable -= Microphone_DataAvailable;
            _microphone.RecordingStopped -= Microphone_RecordingStopped;
            _microphone.StopRecording();
            _microphone.Dispose();
            _microphone = null;
        }

        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _outputBuffer = null;
        StopMonitorOutput();
        SetLevels(0, 0, 0);
    }

    public void ConfigureMonitor(MonitorMode mode, int deviceNumber, float volume)
    {
        lock (_monitorGate)
        {
            var normalizedVolume = ClampGain(volume);
            var shouldRestart = _monitorDeviceNumber != deviceNumber;
            var wasDisabled = _monitorMode == MonitorMode.None;
            _monitorMode = mode;
            _monitorDeviceNumber = deviceNumber;
            _monitorVolume = normalizedVolume;

            if (_monitorMode == MonitorMode.None)
            {
                StopMonitorOutputLocked();
                return;
            }

            if (_outputBuffer is not null && (shouldRestart || wasDisabled || _monitorOutput is null))
            {
                StopMonitorOutputLocked();
                StartMonitorOutputLocked();
            }
        }
    }

    public Guid PlayMp3(string path, bool loop, float volume)
    {
        var player = new Mp3ClipPlayer();
        player.Load(path, SampleRate, loop);

        var playback = new ActiveClipPlayback(Guid.NewGuid(), path, player, ClampGain(volume));
        lock (_clipsGate)
        {
            _activeClips.Add(playback);
        }

        return playback.Id;
    }

    public void StopMp3(Guid playbackId)
    {
        ActiveClipPlayback? stoppedPlayback = null;
        lock (_clipsGate)
        {
            var playback = _activeClips.FirstOrDefault(clip => clip.Id == playbackId);
            if (playback is null)
            {
                return;
            }

            _activeClips.Remove(playback);
            stoppedPlayback = playback;
        }

        stoppedPlayback.Dispose();
        OnClipPlaybackEnded(playbackId, requestedStop: true);
    }

    public void StopAllMp3()
    {
        List<ActiveClipPlayback> stoppedPlaybacks;
        lock (_clipsGate)
        {
            stoppedPlaybacks = _activeClips.ToList();
            _activeClips.Clear();
        }

        foreach (var playback in stoppedPlaybacks)
        {
            playback.Dispose();
            OnClipPlaybackEnded(playback.Id, requestedStop: true);
        }
    }

    public bool IsClipPlaying(Guid playbackId)
    {
        lock (_clipsGate)
        {
            return _activeClips.Any(clip => clip.Id == playbackId && clip.IsPlaying);
        }
    }

    public void SetClipVolume(Guid playbackId, float volume)
    {
        lock (_clipsGate)
        {
            var playback = _activeClips.FirstOrDefault(clip => clip.Id == playbackId);
            if (playback is not null)
            {
                playback.Volume = ClampGain(volume);
            }
        }
    }

    public void SetClipLoop(Guid playbackId, bool loop)
    {
        lock (_clipsGate)
        {
            _activeClips.FirstOrDefault(clip => clip.Id == playbackId)?.Player.SetLoop(loop);
        }
    }

    public TimeSpan GetClipPosition(Guid playbackId)
    {
        lock (_clipsGate)
        {
            return _activeClips.FirstOrDefault(clip => clip.Id == playbackId)?.Player.Position ?? TimeSpan.Zero;
        }
    }

    public void SeekClip(Guid playbackId, TimeSpan position)
    {
        lock (_clipsGate)
        {
            _activeClips.FirstOrDefault(clip => clip.Id == playbackId)?.Player.Seek(position);
        }
    }

    public AudioLevels GetLevels()
    {
        lock (_levelGate)
        {
            return _levels;
        }
    }

    private void Microphone_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_outputBuffer is null)
        {
            return;
        }

        var sampleCount = e.BytesRecorded / 2;
        EnsureBuffers(sampleCount);
        MixActiveClips(sampleCount);

        var micTrimGain = DbToLinear(MicInputTrimDb);
        var micBaseGain = MicMuted ? 0.0f : ClampGain(MicVolume) * micTrimGain;
        var micMixerGain = micBaseGain;
        if (DuckingEnabled && ActiveClipCount > 0)
        {
            micMixerGain *= DbToLinear(DuckingAmountDb);
        }

        var mp3Gain = ClampGain(Mp3Volume);
        var monitorMode = MonitorMode.None;
        var monitorVolume = 0.0f;
        BufferedWaveProvider? monitorBuffer = null;
        lock (_monitorGate)
        {
            if (_monitorOutput is not null && _monitorBuffer is not null)
            {
                monitorMode = _monitorMode;
                monitorVolume = _monitorVolume;
                monitorBuffer = _monitorBuffer;
            }
        }

        var micSumSquares = 0.0;
        var clipsSumSquares = 0.0;
        var outputSumSquares = 0.0;
        var micPeak = 0.0;
        var clipsPeak = 0.0;
        var outputPeak = 0.0;

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var inputOffset = sample * 2;
            var rawMicSample = BitConverter.ToInt16(e.Buffer, inputOffset) / 32768.0f;
            var meteredMicSample = rawMicSample * micBaseGain;
            var mixedMicSample = rawMicSample * micMixerGain;
            var clipsSample = _mp3Buffer[sample] * mp3Gain;
            var mixed = mixedMicSample + clipsSample;
            mixed = SoftLimit(mixed);
            var monitorSample = monitorMode switch
            {
                MonitorMode.Mp3Only => SoftLimit(clipsSample),
                MonitorMode.Mixed => mixed,
                _ => 0.0f
            };
            monitorSample = SoftLimit(monitorSample * monitorVolume);
            micSumSquares += meteredMicSample * meteredMicSample;
            clipsSumSquares += clipsSample * clipsSample;
            outputSumSquares += mixed * mixed;
            micPeak = Math.Max(micPeak, Math.Abs(meteredMicSample));
            clipsPeak = Math.Max(clipsPeak, Math.Abs(clipsSample));
            outputPeak = Math.Max(outputPeak, Math.Abs(mixed));

            var outputSample = (short)Math.Clamp(
                mixed * short.MaxValue,
                short.MinValue,
                short.MaxValue);

            _mixBuffer[inputOffset] = (byte)(outputSample & 0xff);
            _mixBuffer[inputOffset + 1] = (byte)((outputSample >> 8) & 0xff);

            if (monitorBuffer is not null && monitorMode != MonitorMode.None)
            {
                var monitorOutputSample = (short)Math.Clamp(
                    monitorSample * short.MaxValue,
                    short.MinValue,
                    short.MaxValue);

                _monitorMixBuffer[inputOffset] = (byte)(monitorOutputSample & 0xff);
                _monitorMixBuffer[inputOffset + 1] = (byte)((monitorOutputSample >> 8) & 0xff);
            }
        }

        _outputBuffer.AddSamples(_mixBuffer, 0, sampleCount * 2);
        if (monitorBuffer is not null && monitorMode != MonitorMode.None)
        {
            monitorBuffer.AddSamples(_monitorMixBuffer, 0, sampleCount * 2);
        }

        SetLevels(
            CalculateMeterLevel(micSumSquares, micPeak, sampleCount),
            CalculateMeterLevel(clipsSumSquares, clipsPeak, sampleCount),
            CalculateMeterLevel(outputSumSquares, outputPeak, sampleCount));
    }

    private void MixActiveClips(int sampleCount)
    {
        Array.Clear(_mp3Buffer, 0, sampleCount);
        List<Guid>? endedPlaybackIds = null;

        lock (_clipsGate)
        {
            for (var index = _activeClips.Count - 1; index >= 0; index--)
            {
                var playback = _activeClips[index];
                Array.Clear(_clipReadBuffer, 0, sampleCount);
                playback.Player.Read(_clipReadBuffer, 0, sampleCount);

                if (!playback.IsPlaying)
                {
                    _activeClips.RemoveAt(index);
                    playback.Dispose();
                    endedPlaybackIds ??= new List<Guid>();
                    endedPlaybackIds.Add(playback.Id);
                }

                for (var sample = 0; sample < sampleCount; sample++)
                {
                    _mp3Buffer[sample] += _clipReadBuffer[sample] * playback.Volume;
                }
            }
        }

        if (endedPlaybackIds is null)
        {
            return;
        }

        foreach (var playbackId in endedPlaybackIds)
        {
            OnClipPlaybackEnded(playbackId, requestedStop: false);
        }
    }

    private void EnsureBuffers(int sampleCount)
    {
        if (_mp3Buffer.Length < sampleCount)
        {
            _mp3Buffer = new float[sampleCount];
        }

        if (_clipReadBuffer.Length < sampleCount)
        {
            _clipReadBuffer = new float[sampleCount];
        }

        var byteCount = sampleCount * 2;
        if (_mixBuffer.Length < byteCount)
        {
            _mixBuffer = new byte[byteCount];
        }

        if (_monitorMixBuffer.Length < byteCount)
        {
            _monitorMixBuffer = new byte[byteCount];
        }
    }

    private void StartMonitorIfNeeded()
    {
        lock (_monitorGate)
        {
            if (_monitorMode != MonitorMode.None)
            {
                StartMonitorOutputLocked();
            }
        }
    }

    private void StartMonitorOutputLocked()
    {
        if (_monitorOutput is not null)
        {
            return;
        }

        _monitorBuffer = new BufferedWaveProvider(_format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };

        _monitorOutput = new WaveOutEvent
        {
            DeviceNumber = _monitorDeviceNumber,
            DesiredLatency = 80
        };
        _monitorOutput.Init(_monitorBuffer);
        _monitorOutput.Play();
    }

    private void StopMonitorOutput()
    {
        lock (_monitorGate)
        {
            StopMonitorOutputLocked();
        }
    }

    private void StopMonitorOutputLocked()
    {
        _monitorOutput?.Stop();
        _monitorOutput?.Dispose();
        _monitorOutput = null;
        _monitorBuffer = null;
    }

    private static float ClampGain(float value) => Math.Clamp(value, 0.0f, 2.0f);

    private static float DbToLinear(float db) => MathF.Pow(10.0f, db / 20.0f);

    private static float SoftLimit(float sample)
    {
        if (sample is > -1.0f and < 1.0f)
        {
            return sample;
        }

        return MathF.Tanh(sample);
    }

    private void Microphone_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Stop();
        }
    }

    private void OnClipPlaybackEnded(Guid playbackId, bool requestedStop)
    {
        ClipPlaybackEnded?.Invoke(this, new ClipPlaybackEndedEventArgs(playbackId, requestedStop));
    }

    private void SetLevels(double micLevel, double clipsLevel, double outputLevel)
    {
        lock (_levelGate)
        {
            _levels = new AudioLevels(
                Math.Clamp(micLevel, 0.0, 1.0),
                Math.Clamp(clipsLevel, 0.0, 1.0),
                Math.Clamp(outputLevel, 0.0, 1.0));
        }
    }

    private static double CalculateMeterLevel(double sumSquares, double peak, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return 0;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        return Math.Clamp(Math.Max(rms * 4.2, peak * 1.8), 0.0, 1.0);
    }

    public void Dispose()
    {
        Stop();
        StopAllMp3();
    }
}
