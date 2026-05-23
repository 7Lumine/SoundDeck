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
        SelectLatency(settings.VcOutputLatencyMilliseconds);
        LowLatencyModeCheck.IsChecked = settings.LowLatencyMode;
        _isLoading = false;
        UpdateValueText();
    }

    public double MicMeterSensitivity => MicMeterSlider.Value;
    public double ClipsMeterSensitivity => ClipsMeterSlider.Value;
    public double OutputMeterSensitivity => OutputMeterSlider.Value;
    public int VcOutputLatencyMilliseconds => GetSelectedLatency();
    public bool LowLatencyMode => LowLatencyModeCheck.IsChecked == true;
    public event EventHandler? TestToneRequested;
    public event EventHandler? SilenceTestRequested;

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

    private void LowLatencyMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (LowLatencyModeCheck.IsChecked == true && GetSelectedLatency() > 80)
        {
            SelectLatency(80);
        }
        else if (LowLatencyModeCheck.IsChecked != true && GetSelectedLatency() < 100)
        {
            SelectLatency(200);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        MicMeterSlider.Value = 1.0;
        ClipsMeterSlider.Value = 1.0;
        OutputMeterSlider.Value = 1.0;
        SelectLatency(200);
        LowLatencyModeCheck.IsChecked = false;
        UpdateValueText();
    }

    private void TestTone_Click(object sender, RoutedEventArgs e)
    {
        TestToneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SilenceTest_Click(object sender, RoutedEventArgs e)
    {
        SilenceTestRequested?.Invoke(this, EventArgs.Empty);
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

    private void SelectLatency(int latencyMilliseconds)
    {
        var normalized = NormalizeLatency(latencyMilliseconds);
        foreach (var item in VcLatencyCombo.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var value) && value == normalized)
            {
                VcLatencyCombo.SelectedItem = item;
                return;
            }
        }

        VcLatencyCombo.SelectedIndex = 4;
    }

    private int GetSelectedLatency()
    {
        if (VcLatencyCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var value))
        {
            return NormalizeLatency(value);
        }

        return 200;
    }

    private static int NormalizeLatency(int latencyMilliseconds)
    {
        return latencyMilliseconds is 50 or 80 or 100 or 150 or 200 or 300
            ? latencyMilliseconds
            : 200;
    }
}
