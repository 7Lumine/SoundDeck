using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace VoicePipe;

public sealed class SoundClip : INotifyPropertyChanged
{
    private string _name;
    private bool _isPlaying;
    private double _volume = 75;
    private Guid? _playbackId;
    private TimeSpan _position;
    private bool _isSeeking;
    private bool _isLoopEnabled;
    private bool _isPinned;
    private string _hotkeyText = string.Empty;
    private bool _isHotkeyRegistered;

    public SoundClip(string path, TimeSpan? duration, string? displayName = null, string? originalFileName = null)
    {
        Path = path;
        _name = NormalizeDisplayName(displayName, path);
        OriginalFileName = string.IsNullOrWhiteSpace(originalFileName)
            ? System.IO.Path.GetFileName(path)
            : originalFileName.Trim();
        Duration = duration;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }
    public string OriginalFileName { get; }

    public string Name
    {
        get => _name;
        set
        {
            var normalized = NormalizeDisplayName(value, Path);
            if (_name == normalized)
            {
                return;
            }

            _name = normalized;
            OnPropertyChanged();
        }
    }

    public TimeSpan? Duration { get; }
    public double DurationSeconds => Duration?.TotalSeconds ?? 0;

    public string DurationText =>
        Duration is null ? "--:--" : $"{(int)Duration.Value.TotalMinutes:00}:{Duration.Value.Seconds:00}";

    public string FileName => System.IO.Path.GetFileName(Path);
    public string PositionText => $"{(int)Position.TotalMinutes:00}:{Position.Seconds:00}";
    public string StatusTimeText => $"{StateText}  {PositionText} / {DurationText}";

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(StatusTimeText));
        }
    }

    public string StateText => IsPlaying ? "再生中" : "停止中";

    public TimeSpan Position
    {
        get => _position;
        set
        {
            var normalized = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            if (Duration is TimeSpan duration && duration > TimeSpan.Zero && normalized > duration)
            {
                normalized = duration;
            }

            if (_position == normalized)
            {
                return;
            }

            _position = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PositionSeconds));
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(StatusTimeText));
        }
    }

    public double PositionSeconds
    {
        get => Position.TotalSeconds;
        set => Position = TimeSpan.FromSeconds(Math.Clamp(value, 0, Math.Max(0, DurationSeconds)));
    }

    public bool IsSeeking
    {
        get => _isSeeking;
        set
        {
            if (_isSeeking == value)
            {
                return;
            }

            _isSeeking = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoopEnabled
    {
        get => _isLoopEnabled;
        set
        {
            if (_isLoopEnabled == value)
            {
                return;
            }

            _isLoopEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value)
            {
                return;
            }

            _isPinned = value;
            OnPropertyChanged();
        }
    }

    public string HotkeyText
    {
        get => _hotkeyText;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_hotkeyText == normalized)
            {
                return;
            }

            _hotkeyText = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplayText));
            OnPropertyChanged(nameof(HasHotkey));
        }
    }

    public string HotkeyDisplayText => string.IsNullOrWhiteSpace(HotkeyText) ? "未設定" : HotkeyText;
    public bool HasHotkey => !string.IsNullOrWhiteSpace(HotkeyText);

    public bool IsHotkeyRegistered
    {
        get => _isHotkeyRegistered;
        set
        {
            if (_isHotkeyRegistered == value)
            {
                return;
            }

            _isHotkeyRegistered = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (Math.Abs(_volume - normalized) < 0.01)
            {
                return;
            }

            _volume = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Volume:0}%";

    public Guid? PlaybackId
    {
        get => _playbackId;
        set
        {
            if (_playbackId == value)
            {
                return;
            }

            _playbackId = value;
            OnPropertyChanged();
        }
    }

    private static string NormalizeDisplayName(string? value, string path)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
