using System.Windows;
using System.Windows.Controls;
using VoicePipe.Settings;

namespace VoicePipe;

public partial class SettingsDialog : Window
{
    private bool _isLoading = true;

    public SettingsDialog(UserSettings settings)
    {
        InitializeComponent();
        _isLoading = true;
        MicMeterSlider.Value = NormalizeSensitivity(settings.MicMeterSensitivity);
        ClipsMeterSlider.Value = NormalizeSensitivity(settings.ClipsMeterSensitivity);
        OutputMeterSlider.Value = NormalizeSensitivity(settings.OutputMeterSensitivity);
        _isLoading = false;
        UpdateValueText();
    }

    public double MicMeterSensitivity => MicMeterSlider.Value;
    public double ClipsMeterSensitivity => ClipsMeterSlider.Value;
    public double OutputMeterSensitivity => OutputMeterSlider.Value;

    private void MeterSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        if (sender is Slider slider)
        {
            slider.Value = Math.Round(slider.Value, 1);
        }

        UpdateValueText();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        MicMeterSlider.Value = 1.0;
        ClipsMeterSlider.Value = 1.0;
        OutputMeterSlider.Value = 1.0;
        UpdateValueText();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseDialog_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateValueText()
    {
        MicMeterValueText.Text = $"{MicMeterSlider.Value:0.0}x";
        ClipsMeterValueText.Text = $"{ClipsMeterSlider.Value:0.0}x";
        OutputMeterValueText.Text = $"{OutputMeterSlider.Value:0.0}x";
    }

    private static double NormalizeSensitivity(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return 1.0;
        }

        return Math.Clamp(value, 0.5, 4.0);
    }
}
