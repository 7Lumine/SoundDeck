using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace VoicePipe.ViewModels;

public sealed class LevelSegmentViewModel : INotifyPropertyChanged
{
    private static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(30, 40, 51));
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(52, 199, 89));
    private static readonly Brush YellowBrush = new SolidColorBrush(Color.FromRgb(246, 195, 67));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(255, 77, 79));

    private bool _isLit;

    public LevelSegmentViewModel(int topIndex, int segmentCount)
    {
        TopIndex = topIndex;
        SegmentCount = segmentCount;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int TopIndex { get; }
    public int SegmentCount { get; }

    public bool IsLit
    {
        get => _isLit;
        set
        {
            if (_isLit == value)
            {
                return;
            }

            _isLit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
        }
    }

    public Brush Fill
    {
        get
        {
            if (!IsLit)
            {
                return OffBrush;
            }

            var bottomIndex = SegmentCount - TopIndex - 1;
            var ratio = (bottomIndex + 1.0) / SegmentCount;
            if (ratio > 0.9)
            {
                return RedBrush;
            }

            if (ratio > 0.7)
            {
                return YellowBrush;
            }

            return GreenBrush;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
