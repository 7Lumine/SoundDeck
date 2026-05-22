using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoicePipe.Audio;

public sealed class VoicePipeEngine : IDisposable
{
    public const int SampleRate = 48_000;
    private const int CaptureBitsPerSample = 16;
    private const int CaptureChannels = 1;
    private const int OutputChannels = 2;
    private const int BufferMilliseconds = 20;

    private static readonly WaveFormat CaptureFormat = new(SampleRate, CaptureBitsPerSample, CaptureChannels);
    private static readonly WaveFormat OutputPcmFormat = new(SampleRate, 16, OutputChannels);

    private readonly object _clipsGate = new();
    private readonly List<ActiveClipPlayback> _activeClips = new();
    private readonly object _monitorGate = new();
    private readonly object _levelGate = new();
    private readonly FloatRingBuffer _micRing = new(SampleRate * 2);

    private WaveInEvent? _microphone;
    private WaveOutEvent? _output;
    private SoundDeckMixerSampleProvider? _mainMixer;
    private WaveOutEvent? _monitorOutput;
    private BufferedWaveProvider? _monitorBuffer;
    private float[] _captureSampleBuffer = Array.Empty<float>();
    private byte[] _monitorByteBuffer = Array.Empty<byte>();
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
    public int OutputLatencyMilliseconds { get; set; } = 200;
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
        _micRing.Clear();

        _mainMixer = new SoundDeckMixerSampleProvider(this, SampleRate);
        _output = new WaveOutEvent
        {
            DeviceNumber = outputDeviceNumber,
            DesiredLatency = NormalizeLatency(OutputLatencyMilliseconds)
        };
        _output.Init(new SampleToWaveProvider16(_mainMixer));

        _microphone = new WaveInEvent
        {
            DeviceNumber = inputDeviceNumber,
            WaveFormat = CaptureFormat,
            BufferMilliseconds = BufferMilliseconds,
            NumberOfBuffers = 4
        };
        _microphone.DataAvailable += Microphone_DataAvailable;
        _microphone.RecordingStopped += Microphone_RecordingStopped;

        Log($"VC output start: device={outputDeviceNumber}, engine=WaveOutEvent, sampleRate={SampleRate}, channels={OutputChannels}, internal=float32, outputFormat={OutputPcmFormat}, latencyMs={_output.DesiredLatency}, continuousStream=true, mixerReadFully=true");
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
        _mainMixer = null;
        _micRing.Clear();
        StopMonitorOutput();
        SetLevels(0, 0, 0);
        Log("VC output stop");
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

            if (_output is not null && (shouldRestart || wasDisabled || _monitorOutput is null))
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
            Log($"Clip start: id={playback.Id}, file={Path.GetFileName(path)}, original={player.OriginalSampleRate}Hz/{player.OriginalChannels}ch, resampled={SampleRate}Hz/{OutputChannels}ch, activeInputs={_activeClips.Count}");
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
            Log($"Clip stop: id={playbackId}, activeInputs={_activeClips.Count}, vcOutputRecreated=false");
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
            Log($"All clips stop: count={stoppedPlaybacks.Count}, vcOutputRecreated=false");
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

    public static async Task RunDiagnosticToneAsync(int outputDeviceNumber, int latencyMilliseconds, CancellationToken cancellationToken = default)
    {
        await RunDiagnosticSignalAsync(outputDeviceNumber, latencyMilliseconds, tone: true, cancellationToken).ConfigureAwait(false);
    }

    public static async Task RunDiagnosticSilenceAsync(int outputDeviceNumber, int latencyMilliseconds, CancellationToken cancellationToken = default)
    {
        await RunDiagnosticSignalAsync(outputDeviceNumber, latencyMilliseconds, tone: false, cancellationToken).ConfigureAwait(false);
    }

    internal int ReadMicSamples(float[] buffer, int offset, int count)
    {
        return _micRing.Read(buffer, offset, count);
    }

    internal void MixActiveClips(float[] destination, int offset, int count, float[] scratchBuffer)
    {
        Array.Clear(destination, offset, count);
        List<Guid>? endedPlaybackIds = null;

        lock (_clipsGate)
        {
            for (var index = _activeClips.Count - 1; index >= 0; index--)
            {
                var playback = _activeClips[index];
                Array.Clear(scratchBuffer, 0, count);
                playback.Player.Read(scratchBuffer, 0, count);

                if (!playback.IsPlaying)
                {
                    _activeClips.RemoveAt(index);
                    playback.Dispose();
                    endedPlaybackIds ??= new List<Guid>();
                    endedPlaybackIds.Add(playback.Id);
                    Log($"Clip natural end: id={playback.Id}, activeInputs={_activeClips.Count}, vcOutputRecreated=false");
                    continue;
                }

                for (var sample = 0; sample < count; sample++)
                {
                    destination[offset + sample] += scratchBuffer[sample] * playback.Volume;
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

    internal MonitorState GetMonitorState()
    {
        lock (_monitorGate)
        {
            return new MonitorState(_monitorMode, _monitorVolume, _monitorOutput is not null && _monitorBuffer is not null);
        }
    }

    internal void WriteMonitorSamples(float[] samples, int offset, int count)
    {
        BufferedWaveProvider? monitorBuffer;
        lock (_monitorGate)
        {
            monitorBuffer = _monitorBuffer;
        }

        if (monitorBuffer is null)
        {
            return;
        }

        var byteCount = count * 2;
        if (_monitorByteBuffer.Length < byteCount)
        {
            _monitorByteBuffer = new byte[byteCount];
        }

        for (var sample = 0; sample < count; sample++)
        {
            var pcm = (short)Math.Clamp(samples[offset + sample] * short.MaxValue, short.MinValue, short.MaxValue);
            var byteOffset = sample * 2;
            _monitorByteBuffer[byteOffset] = (byte)(pcm & 0xff);
            _monitorByteBuffer[byteOffset + 1] = (byte)((pcm >> 8) & 0xff);
        }

        monitorBuffer.AddSamples(_monitorByteBuffer, 0, byteCount);
    }

    internal void SetLevelsFromAudioThread(double micLevel, double clipsLevel, double outputLevel)
    {
        SetLevels(micLevel, clipsLevel, outputLevel);
    }

    private void Microphone_DataAvailable(object? sender, WaveInEventArgs e)
    {
        var sampleCount = e.BytesRecorded / 2;
        if (_captureSampleBuffer.Length < sampleCount)
        {
            _captureSampleBuffer = new float[sampleCount];
        }

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var inputOffset = sample * 2;
            _captureSampleBuffer[sample] = BitConverter.ToInt16(e.Buffer, inputOffset) / 32768.0f;
        }

        _micRing.Write(_captureSampleBuffer, 0, sampleCount);
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

        _monitorBuffer = new BufferedWaveProvider(OutputPcmFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };

        _monitorOutput = new WaveOutEvent
        {
            DeviceNumber = _monitorDeviceNumber,
            DesiredLatency = 120
        };
        _monitorOutput.Init(_monitorBuffer);
        _monitorOutput.Play();
        Log($"Monitor start: mode={_monitorMode}, device={_monitorDeviceNumber}, format={OutputPcmFormat}");
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

    internal static float ClampGain(float value) => Math.Clamp(value, 0.0f, 2.0f);

    internal static float DbToLinear(float db) => MathF.Pow(10.0f, db / 20.0f);

    internal static float SoftLimit(float sample)
    {
        if (sample is > -1.0f and < 1.0f)
        {
            return sample;
        }

        return MathF.Tanh(sample);
    }

    internal static double CalculateMeterLevel(double sumSquares, double peak, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return 0;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        return Math.Clamp(Math.Max(rms * 4.2, peak * 1.8), 0.0, 1.0);
    }

    private void Microphone_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log($"Microphone stopped with error: {e.Exception.Message}");
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

    private static int NormalizeLatency(int latencyMilliseconds)
    {
        return latencyMilliseconds is 100 or 150 or 200 or 300
            ? latencyMilliseconds
            : 200;
    }

    private static async Task RunDiagnosticSignalAsync(int outputDeviceNumber, int latencyMilliseconds, bool tone, CancellationToken cancellationToken)
    {
        using var output = new WaveOutEvent
        {
            DeviceNumber = outputDeviceNumber,
            DesiredLatency = NormalizeLatency(latencyMilliseconds)
        };
        var provider = new DiagnosticSignalSampleProvider(tone, SampleRate, OutputChannels);
        output.Init(new SampleToWaveProvider16(provider));
        Log($"Diagnostic {(tone ? "tone" : "silence")} start: device={outputDeviceNumber}, sampleRate={SampleRate}, channels={OutputChannels}, latencyMs={output.DesiredLatency}");
        output.Play();
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        output.Stop();
        Log($"Diagnostic {(tone ? "tone" : "silence")} stop");
    }

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundDeck");
            Directory.CreateDirectory(directory);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "sounddeck.log"), line);
            Debug.WriteLine(line);
        }
        catch
        {
            // Diagnostics must never interrupt audio routing.
        }
    }

    public void Dispose()
    {
        Stop();
        StopAllMp3();
    }

    internal readonly record struct MonitorState(MonitorMode Mode, float Volume, bool Enabled);
}
