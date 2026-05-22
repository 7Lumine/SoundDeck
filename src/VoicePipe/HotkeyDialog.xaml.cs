using System.Windows;
using System.Windows.Input;
using VoicePipe.Hotkeys;

namespace VoicePipe;

public partial class HotkeyDialog : Window
{
    public HotkeyDialog(string clipName, string currentHotkey)
    {
        InitializeComponent();
        ClipNameText.Text = $"対象: {clipName}";
        HotkeyText = currentHotkey.Trim();
        UpdateDisplay();
        Loaded += (_, _) => CaptureBox.Focus();
    }

    public string HotkeyText { get; private set; }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        if (HotkeyDefinition.FromKeyEvent(e) is not HotkeyDefinition hotkey)
        {
            return;
        }

        HotkeyText = hotkey.DisplayText;
        UpdateDisplay();
        e.Handled = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        HotkeyText = string.Empty;
        UpdateDisplay();
        CaptureBox.Focus();
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

    private void UpdateDisplay()
    {
        HotkeyTextBlock.Text = string.IsNullOrWhiteSpace(HotkeyText)
            ? "未設定"
            : HotkeyText;
    }
}
