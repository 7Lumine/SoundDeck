using System.Windows;
using VoicePipe.Audio;

namespace VoicePipe;

public partial class MonitorOutputDialog : Window
{
    public MonitorOutputDialog(IReadOnlyList<AudioDeviceInfo> devices, AudioDeviceInfo? currentDevice)
    {
        InitializeComponent();
        OutputDeviceCombo.ItemsSource = devices;
        SelectedDevice = currentDevice ?? devices.FirstOrDefault();

        if (SelectedDevice is not null)
        {
            for (var index = 0; index < OutputDeviceCombo.Items.Count; index++)
            {
                if (OutputDeviceCombo.Items[index] is AudioDeviceInfo device &&
                    device.DeviceNumber == SelectedDevice.DeviceNumber &&
                    string.Equals(device.Name, SelectedDevice.Name, StringComparison.OrdinalIgnoreCase))
                {
                    OutputDeviceCombo.SelectedIndex = index;
                    break;
                }
            }
        }

        if (OutputDeviceCombo.SelectedIndex < 0 && OutputDeviceCombo.Items.Count > 0)
        {
            OutputDeviceCombo.SelectedIndex = 0;
        }
    }

    public AudioDeviceInfo? SelectedDevice { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedDevice = OutputDeviceCombo.SelectedItem as AudioDeviceInfo;
        DialogResult = SelectedDevice is not null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseDialog_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
