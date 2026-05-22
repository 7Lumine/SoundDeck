using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoicePipe.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int SegmentCount = 8;
    private double _micLevel;
    private double _clipsLevel;
    private double _outputLevel;

    public MainWindowViewModel()
    {
        MicSegments = CreateSegments();
        ClipsSegments = CreateSegments();
        OutputSegments = CreateSegments();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SoundClip> Clips { get; } = new();
    public ObservableCollection<SoundClip> DisplayedClips { get; } = new();
    public ObservableCollection<SoundClip> ActiveClips { get; } = new();
    public ObservableCollection<LevelSegmentViewModel> MicSegments { get; }
    public ObservableCollection<LevelSegmentViewModel> ClipsSegments { get; }
    public ObservableCollection<LevelSegmentViewModel> OutputSegments { get; }

    public double MicLevel
    {
        get => _micLevel;
        private set => SetLevel(ref _micLevel, value);
    }

    public double ClipsLevel
    {
        get => _clipsLevel;
        private set => SetLevel(ref _clipsLevel, value);
    }

    public double OutputLevel
    {
        get => _outputLevel;
        private set => SetLevel(ref _outputLevel, value);
    }

    public void SetLevels(double micLevel, double clipsLevel, double outputLevel)
    {
        MicLevel = micLevel;
        ClipsLevel = clipsLevel;
        OutputLevel = outputLevel;
        UpdateSegments(MicSegments, MicLevel);
        UpdateSegments(ClipsSegments, ClipsLevel);
        UpdateSegments(OutputSegments, OutputLevel);
    }

    private static ObservableCollection<LevelSegmentViewModel> CreateSegments()
    {
        var segments = new ObservableCollection<LevelSegmentViewModel>();
        for (var index = 0; index < SegmentCount; index++)
        {
            segments.Add(new LevelSegmentViewModel(index, SegmentCount));
        }

        return segments;
    }

    private static void UpdateSegments(IEnumerable<LevelSegmentViewModel> segments, double level)
    {
        var litCount = (int)Math.Ceiling(Math.Clamp(level, 0.0, 1.0) * SegmentCount);
        foreach (var segment in segments)
        {
            var bottomIndex = SegmentCount - segment.TopIndex - 1;
            segment.IsLit = bottomIndex < litCount;
        }
    }

    private void SetLevel(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(field - normalized) < 0.001)
        {
            return;
        }

        field = normalized;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
