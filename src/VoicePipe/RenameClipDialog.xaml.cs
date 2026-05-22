using System.Windows;

namespace VoicePipe;

public partial class RenameClipDialog : Window
{
    public RenameClipDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ClipName => NameBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ClipName))
        {
            MessageBox.Show(this, "クリップ名を入力してください。", "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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
}
