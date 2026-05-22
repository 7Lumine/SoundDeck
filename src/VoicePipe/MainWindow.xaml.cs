using Microsoft.Win32;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VoicePipe.Audio;
using VoicePipe.Hotkeys;
using VoicePipe.Library;
using VoicePipe.Settings;
using VoicePipe.ViewModels;

namespace VoicePipe;

public partial class MainWindow : Window
{
    private const string SearchPlaceholder = "クリップを検索...";
    private readonly AudioDeviceService _devices = new();
    private readonly VoicePipeEngine _engine = new();
    private readonly UserSettings _settings = UserSettingsStore.Load();
    private readonly MainWindowViewModel _viewModel = new();
    private readonly DispatcherTimer _uiTimer = new();
    private readonly HotkeyManager _hotkeys = new();
    private HwndSource? _windowSource;
    private bool _isLoadingSettings;
    private double _displayedMicLevel;
    private double _displayedClipsLevel;
    private double _displayedOutputLevel;
    private bool _isUpdatingSelectedClipToolbar;

    public ObservableCollection<SoundClip> Clips => _viewModel.Clips;
    public ObservableCollection<SoundClip> ActiveClips => _viewModel.ActiveClips;
    private SoundClip? _selectedClip;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeRestoreButton();
        _engine.ClipPlaybackEnded += Engine_ClipPlaybackEnded;
        ApplySavedControlValues();
        var recoveredLibraryClips = LoadSavedClips();
        RefreshDevices();
        ApplyAudioOptions();
        if (recoveredLibraryClips)
        {
            SaveSettings();
        }

        _uiTimer.Interval = TimeSpan.FromMilliseconds(33);
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
        Clips.CollectionChanged += (_, _) =>
        {
            UpdateClipEmptyState();
            RefreshClipSections();
        };
        UpdateClipEmptyState();
        RefreshClipSections();
        UpdateSelectedClipToolbar();
        UpdateMaximizeRestoreButton();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotkeys.Initialize(handle);
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        RegisterHotkeys(showFailures: true);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != HotkeyNative.WmHotkey)
        {
            return IntPtr.Zero;
        }

        var clip = _hotkeys.GetClip(wParam.ToInt32());
        if (clip is null)
        {
            return IntPtr.Zero;
        }

        PlayClip(clip);
        handled = true;
        return IntPtr.Zero;
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshClipSections();
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (SearchBox.Text != SearchPlaceholder)
        {
            return;
        }

        SearchBox.Text = string.Empty;
        SearchBox.Foreground = (Brush)FindResource("PrimaryTextBrush");
    }

    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            return;
        }

        SearchBox.Text = SearchPlaceholder;
        SearchBox.Foreground = (Brush)FindResource("MutedTextBrush");
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings)
        {
            Owner = this
        };
        dialog.TestToneRequested += SettingsDialog_TestToneRequested;
        dialog.SilenceTestRequested += SettingsDialog_SilenceTestRequested;

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings.MicMeterSensitivity = dialog.MicMeterSensitivity;
        _settings.ClipsMeterSensitivity = dialog.ClipsMeterSensitivity;
        _settings.OutputMeterSensitivity = dialog.OutputMeterSensitivity;
        _settings.VcOutputLatencyMilliseconds = dialog.VcOutputLatencyMilliseconds;
        _engine.OutputLatencyMilliseconds = _settings.VcOutputLatencyMilliseconds;
        _displayedMicLevel = 0;
        _displayedClipsLevel = 0;
        _displayedOutputLevel = 0;
        SaveSettings();
        SetStatus("Settings saved", TopStatusText.Text);
    }

    private void SettingsDialog_TestToneRequested(object? sender, EventArgs e)
    {
        RunOutputDiagnostic(tone: true, sender is SettingsDialog dialog ? dialog.VcOutputLatencyMilliseconds : null);
    }

    private void SettingsDialog_SilenceTestRequested(object? sender, EventArgs e)
    {
        RunOutputDiagnostic(tone: false, sender is SettingsDialog dialog ? dialog.VcOutputLatencyMilliseconds : null);
    }

    private void RunOutputDiagnostic(bool tone, int? latencyMilliseconds)
    {
        if (OutputDeviceCombo?.SelectedItem is not AudioDeviceInfo output)
        {
            SetStatus("Select VC output first", "Error");
            return;
        }

        var latency = latencyMilliseconds ?? _settings.VcOutputLatencyMilliseconds;
        SetStatus(tone ? "Test tone running for 5 seconds" : "Silence stream test running for 5 seconds", TopStatusText.Text);
        _ = Task.Run(async () =>
        {
            try
            {
                if (tone)
                {
                    await VoicePipeEngine.RunDiagnosticToneAsync(output.DeviceNumber, latency).ConfigureAwait(false);
                }
                else
                {
                    await VoicePipeEngine.RunDiagnosticSilenceAsync(output.DeviceNumber, latency).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetStatus("Diagnostic output failed", "Error");
                    MessageBox.Show(this, ex.Message, "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        });
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (MaximizeRestoreButton is not null)
        {
            var isMaximized = WindowState == WindowState.Maximized;
            MaximizeRestoreButton.ToolTip = isMaximized ? "元に戻す" : "最大化";
            if (MaximizeIcon is not null && RestoreIcon is not null)
            {
                MaximizeIcon.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
                RestoreIcon.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void RefreshDevices()
    {
        _isLoadingSettings = true;
        InputDeviceCombo.ItemsSource = _devices.GetInputDevices();
        OutputDeviceCombo.ItemsSource = _devices.GetOutputDevices();
        MonitorOutputCombo.ItemsSource = _devices.GetOutputDevices(includeDefaultDevice: true);

        SelectSavedOrDefaultDevice(InputDeviceCombo, _settings.Microphone);
        SelectSavedOrDefaultOutput();
        SelectSavedOrDefaultDevice(MonitorOutputCombo, _settings.MonitorOutput);
        _isLoadingSettings = false;

        UpdateRouteDisplay();
        UpdateMonitorWarning();
        SetStatus("Devices refreshed", "Ready");
    }

    private void SelectSavedOrDefaultOutput()
    {
        if (SelectSavedOrDefaultDevice(OutputDeviceCombo, _settings.VcOutput))
        {
            return;
        }

        SelectLikelyCableOutput();
    }

    private static bool SelectSavedOrDefaultDevice(ComboBox comboBox, DeviceSelectionSettings selection)
    {
        if (comboBox.Items.Count == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selection.Name))
        {
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.Items[index] is AudioDeviceInfo device &&
                    string.Equals(device.Name, selection.Name, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = index;
                    return true;
                }
            }
        }

        if (selection.DeviceNumber is int savedNumber)
        {
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.Items[index] is AudioDeviceInfo device &&
                    device.DeviceNumber == savedNumber)
                {
                    comboBox.SelectedIndex = index;
                    return true;
                }
            }
        }

        comboBox.SelectedIndex = 0;
        return false;
    }

    private static void SelectOutputDevice(ComboBox comboBox, AudioDeviceInfo selection)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is AudioDeviceInfo device &&
                device.DeviceNumber == selection.DeviceNumber &&
                string.Equals(device.Name, selection.Name, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        comboBox.SelectedItem = selection;
    }

    private void SelectLikelyCableOutput()
    {
        for (var index = 0; index < OutputDeviceCombo.Items.Count; index++)
        {
            if (OutputDeviceCombo.Items[index] is AudioDeviceInfo device &&
                device.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
            {
                OutputDeviceCombo.SelectedIndex = index;
                return;
            }
        }

        if (OutputDeviceCombo.Items.Count > 0)
        {
            OutputDeviceCombo.SelectedIndex = 0;
        }
    }

    private void ApplySavedControlValues()
    {
        _isLoadingSettings = true;
        MicVolumeSlider.Value = ClampPercent(_settings.MicrophoneVolume, 100);
        Mp3VolumeSlider.Value = ClampPercent(_settings.Mp3Volume, 75);
        MonitorVolumeSlider.Value = ClampPercent(_settings.MonitorVolume, 50);
        MuteMicCheck.IsChecked = _settings.MuteMicrophone;
        DuckingCheck.IsChecked = _settings.DuckingEnabled;
        SelectDuckingAmount(_settings.DuckingAmountDb);
        SelectMonitorMode(ParseMonitorMode(_settings.MonitorMode));
        _isLoadingSettings = false;
    }

    private static double ClampPercent(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, 0, 100);
    }

    private void SelectDuckingAmount(double amountDb)
    {
        if (DuckingAmountCombo is null)
        {
            return;
        }

        var allowed = new[] { -6.0, -9.0, -12.0, -18.0, -24.0 };
        var selected = allowed.MinBy(candidate => Math.Abs(candidate - amountDb));

        for (var index = 0; index < DuckingAmountCombo.Items.Count; index++)
        {
            if (DuckingAmountCombo.Items[index] is ComboBoxItem item &&
                double.TryParse(item.Tag?.ToString(), out var itemValue) &&
                Math.Abs(itemValue - selected) < 0.01)
            {
                DuckingAmountCombo.SelectedIndex = index;
                return;
            }
        }

        DuckingAmountCombo.SelectedIndex = 2;
    }

    private void AddMp3_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add MP3 files",
            Filter = "MP3 files (*.mp3)|*.mp3|Audio files (*.mp3;*.wav)|*.mp3;*.wav|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            string libraryPath;
            var displayName = Path.GetFileNameWithoutExtension(fileName);
            var originalFileName = Path.GetFileName(fileName);
            try
            {
                libraryPath = ClipLibraryService.Import(fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Error);
                continue;
            }

            if (Clips.Any(clip => string.Equals(clip.Path, libraryPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Clips.Add(new SoundClip(libraryPath, TryReadDuration(libraryPath), displayName, originalFileName));
        }

        SetStatus($"Added {dialog.FileNames.Length} file(s)", TopStatusText.Text);
        UpdateActiveClipDisplay();
        SaveSettings();
    }

    private static TimeSpan? TryReadDuration(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime;
        }
        catch
        {
            return null;
        }
    }

    private void RemoveMp3_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClip is not SoundClip selected)
        {
            SetStatus("Select a clip to remove", TopStatusText.Text);
            return;
        }

        StopClip(selected);
        Clips.Remove(selected);
        ClipLibraryService.DeleteLibraryFile(selected.Path);
        _selectedClip = null;
        RegisterHotkeys();
        SetStatus("Removed clip", TopStatusText.Text);
        UpdateSelectedClipToolbar();
        UpdateActiveClipDisplay();
        SaveSettings();
    }

    private void RenameMp3_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClip is not SoundClip selected)
        {
            SetStatus("Select a clip to rename", TopStatusText.Text);
            return;
        }

        var dialog = new RenameClipDialog(selected.Name)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        selected.Name = dialog.ClipName;
        SetStatus($"Renamed: {selected.Name}", TopStatusText.Text);
        SaveClipMetadata(selected);
        UpdateSelectedClipToolbar();
        RefreshClipSections();
        UpdateActiveClipDisplay();
        SaveSettings();
    }

    private void SetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClip is not SoundClip selected)
        {
            SetStatus("Select a clip to assign a hotkey", TopStatusText.Text);
            return;
        }

        var dialog = new HotkeyDialog(selected.Name, selected.HotkeyText)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(dialog.HotkeyText) &&
            Clips.Any(clip => !ReferenceEquals(clip, selected) &&
                              string.Equals(clip.HotkeyText, dialog.HotkeyText, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "同じホットキーが別のクリップに設定されています。", "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        selected.HotkeyText = dialog.HotkeyText;
        SaveClipMetadata(selected);
        RegisterHotkeys(showFailures: true);
        UpdateSelectedClipToolbar();
        RefreshClipSections();
        SaveSettings();
        SetStatus(string.IsNullOrWhiteSpace(selected.HotkeyText)
            ? "Hotkey cleared"
            : $"Hotkey set: {selected.HotkeyText}", TopStatusText.Text);
    }

    private void StartPipe_Click(object sender, RoutedEventArgs e)
    {
        if (InputDeviceCombo.SelectedItem is not AudioDeviceInfo input ||
            OutputDeviceCombo.SelectedItem is not AudioDeviceInfo output)
        {
            SetStatus("Select microphone and output devices", "Error");
            return;
        }

        try
        {
            _engine.OutputLatencyMilliseconds = _settings.VcOutputLatencyMilliseconds;
            _engine.Start(input.DeviceNumber, output.DeviceNumber);
            ApplyAudioOptions();
            SaveSettings();
            UpdateRouteDisplay();
            SetStatus($"Pipe running: {input.Name} -> {output.Name}", "Pipe Running");
        }
        catch (Exception ex)
        {
            SetStatus("Could not start pipe", "Error");
            MessageBox.Show(this, ex.Message, "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopPipe_Click(object sender, RoutedEventArgs e)
    {
        _engine.Stop();
        SetStatus("Pipe stopped", "Ready");
    }

    private void PlayClip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundClip clip)
        {
            PlayClip(clip);
        }
    }

    private void StopClip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundClip clip)
        {
            StopClip(clip);
            SetStatus($"Stopped: {clip.Name}", TopStatusText.Text);
        }
    }

    private void StopAllMp3_Click(object sender, RoutedEventArgs e)
    {
        _engine.StopAllMp3();
        foreach (var clip in Clips)
        {
            clip.PlaybackId = null;
            clip.IsPlaying = false;
            clip.Position = TimeSpan.Zero;
        }

        UpdateActiveClipDisplay();
        SetStatus("All clips stopped", TopStatusText.Text);
    }

    private void PlayClip(SoundClip clip)
    {
        try
        {
            if (clip.PlaybackId is not null)
            {
                StopClip(clip);
            }

            var playbackId = _engine.PlayMp3(
                clip.Path,
                clip.IsLoopEnabled,
                (float)(clip.Volume / 100.0));

            clip.Position = TimeSpan.Zero;
            clip.PlaybackId = playbackId;
            clip.IsPlaying = true;
            UpdateActiveClipDisplay();
            SetStatus($"Playing: {clip.Name}", _engine.IsRunning ? "Pipe Running" : "Ready");
        }
        catch (Exception ex)
        {
            clip.PlaybackId = null;
            clip.IsPlaying = false;
            clip.Position = TimeSpan.Zero;
            SetStatus("Could not play MP3", "Error");
            MessageBox.Show(this, ex.Message, "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopClip(SoundClip clip)
    {
        if (clip.PlaybackId is Guid playbackId)
        {
            _engine.StopMp3(playbackId);
        }

        clip.PlaybackId = null;
        clip.IsPlaying = false;
        clip.Position = TimeSpan.Zero;
        UpdateActiveClipDisplay();
    }

    private void ClipVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundClip clip &&
            clip.PlaybackId is Guid playbackId)
        {
            _engine.SetClipVolume(playbackId, (float)(clip.Volume / 100.0));
        }
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyAudioOptions();
        SaveSettings();
    }

    private void AudioOption_Changed(object sender, RoutedEventArgs e)
    {
        ApplyAudioOptions();
        SaveSettings();
    }

    private void ClipLoop_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || _isUpdatingSelectedClipToolbar || _selectedClip is not SoundClip selected)
        {
            return;
        }

        selected.IsLoopEnabled = LoopCheck?.IsChecked == true;
        if (selected.PlaybackId is Guid playbackId)
        {
            _engine.SetClipLoop(playbackId, selected.IsLoopEnabled);
        }

        SaveSettings();
    }

    private void DuckingAmount_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyAudioOptions();
        SaveSettings();
    }

    private void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyAudioOptions();
        SaveSettings();
    }

    private void MonitorModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        ApplyAudioOptions();
        SaveSettings();
    }

    private void ChangeMonitorOutput_Click(object sender, RoutedEventArgs e)
    {
        var devices = (MonitorOutputCombo.ItemsSource as IEnumerable<AudioDeviceInfo>)?.ToList() ??
                      _devices.GetOutputDevices(includeDefaultDevice: true).ToList();
        var current = MonitorOutputCombo.SelectedItem as AudioDeviceInfo;
        var dialog = new MonitorOutputDialog(devices, current)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.SelectedDevice is not AudioDeviceInfo selected)
        {
            return;
        }

        SelectOutputDevice(MonitorOutputCombo, selected);
        ApplyAudioOptions();
        SaveSettings();
    }

    private void MonitorVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyAudioOptions();
        SaveSettings();
    }

    private void DeviceSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateRouteDisplay();
        UpdateMonitorWarning();
        SaveSettings();
    }

    private void ApplyAudioOptions()
    {
        if (MicVolumeSlider is null || Mp3VolumeSlider is null)
        {
            return;
        }

        _engine.MicVolume = (float)(MicVolumeSlider.Value / 100.0);
        _engine.Mp3Volume = (float)(Mp3VolumeSlider.Value / 100.0);
        _engine.MicMuted = MuteMicCheck?.IsChecked == true;
        _engine.DuckingEnabled = DuckingCheck?.IsChecked == true;
        _engine.DuckingAmountDb = (float)GetSelectedDuckingAmountDb();
        ApplyMonitorOptions();

        if (MicVolumeText is not null)
        {
            MicVolumeText.Text = $"{MicVolumeSlider.Value:0}%";
        }

        if (Mp3VolumeText is not null)
        {
            Mp3VolumeText.Text = $"{Mp3VolumeSlider.Value:0}%";
        }
    }

    private void ApplyMonitorOptions()
    {
        if (MonitorModeOff is null || MonitorOutputCombo is null || MonitorVolumeSlider is null)
        {
            return;
        }

        var mode = GetSelectedMonitorMode();
        var outputDeviceNumber = (MonitorOutputCombo.SelectedItem as AudioDeviceInfo)?.DeviceNumber ?? -1;
        var volume = (float)(MonitorVolumeSlider.Value / 100.0);

        try
        {
            _engine.ConfigureMonitor(mode, outputDeviceNumber, volume);
        }
        catch (Exception ex)
        {
            _engine.ConfigureMonitor(MonitorMode.None, outputDeviceNumber, 0);
            var wasLoadingSettings = _isLoadingSettings;
            _isLoadingSettings = true;
            SelectMonitorMode(MonitorMode.None);
            _isLoadingSettings = wasLoadingSettings;
            SetStatus("Could not start monitor output", "Error");
            MessageBox.Show(this, ex.Message, "SoundDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (MonitorVolumeText is not null)
        {
            MonitorVolumeText.Text = $"{MonitorVolumeSlider.Value:0}%";
        }

        if (MonitorOutputText is not null)
        {
            MonitorOutputText.Text = (MonitorOutputCombo.SelectedItem as AudioDeviceInfo)?.Name ?? "既定の再生デバイス";
        }

        UpdateMonitorWarning();
    }

    private void RegisterHotkeys(bool showFailures = false)
    {
        var failures = _hotkeys.RegisterClips(Clips);
        if (!showFailures || failures.Count == 0)
        {
            return;
        }

        SetStatus(failures[0], "Ready");
    }

    private void SaveSettings()
    {
        if (_isLoadingSettings ||
            MicVolumeSlider is null ||
            Mp3VolumeSlider is null ||
            MuteMicCheck is null ||
            DuckingCheck is null ||
            MonitorModeOff is null ||
            MonitorModeMp3 is null ||
            MonitorModeMixed is null ||
            MonitorOutputCombo is null ||
            MonitorVolumeSlider is null)
        {
            return;
        }

        if (InputDeviceCombo.SelectedItem is AudioDeviceInfo input)
        {
            _settings.Microphone.DeviceNumber = input.DeviceNumber;
            _settings.Microphone.Name = input.Name;
        }

        if (OutputDeviceCombo.SelectedItem is AudioDeviceInfo output)
        {
            _settings.VcOutput.DeviceNumber = output.DeviceNumber;
            _settings.VcOutput.Name = output.Name;
        }

        _settings.MicrophoneVolume = MicVolumeSlider.Value;
        _settings.Mp3Volume = Mp3VolumeSlider.Value;
        _settings.MuteMicrophone = MuteMicCheck?.IsChecked == true;
        _settings.DuckingEnabled = DuckingCheck?.IsChecked == true;
        _settings.DuckingAmountDb = GetSelectedDuckingAmountDb();
        _settings.MonitorMode = GetSelectedMonitorMode().ToString();
        _settings.MonitorVolume = MonitorVolumeSlider.Value;
        if (MonitorOutputCombo.SelectedItem is AudioDeviceInfo monitorOutput)
        {
            _settings.MonitorOutput.DeviceNumber = monitorOutput.DeviceNumber;
            _settings.MonitorOutput.Name = monitorOutput.Name;
        }

        _settings.Clips = Clips
            .Select(clip => new ClipSettings
            {
                Path = clip.Path,
                DisplayName = clip.Name,
                OriginalFileName = clip.OriginalFileName,
                IsLoopEnabled = clip.IsLoopEnabled,
                IsPinned = clip.IsPinned,
                HotkeyText = clip.HotkeyText
            })
            .ToList();
        UserSettingsStore.Save(_settings);

        foreach (var clip in Clips)
        {
            SaveClipMetadata(clip);
        }
    }

    private bool LoadSavedClips()
    {
        var recoveredLibraryClip = false;
        foreach (var clip in _settings.Clips)
        {
            if (string.IsNullOrWhiteSpace(clip.Path) || !File.Exists(clip.Path))
            {
                continue;
            }

            if (Clips.Any(existing => string.Equals(existing.Path, clip.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var metadata = ClipLibraryService.TryReadMetadata(clip.Path);
            Clips.Add(new SoundClip(
                clip.Path,
                TryReadDuration(clip.Path),
                ChooseDisplayName(clip.Path, clip.DisplayName, metadata?.DisplayName),
                ChooseText(clip.OriginalFileName, metadata?.OriginalFileName))
            {
                IsLoopEnabled = clip.IsLoopEnabled || metadata?.IsLoopEnabled == true,
                IsPinned = clip.IsPinned || metadata?.IsPinned == true,
                HotkeyText = ChooseText(clip.HotkeyText, metadata?.HotkeyText)
            });
        }

        foreach (var libraryPath in ClipLibraryService.EnumerateLibraryClips())
        {
            if (Clips.Any(existing => string.Equals(existing.Path, libraryPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var metadata = ClipLibraryService.TryReadMetadata(libraryPath);
            Clips.Add(new SoundClip(
                libraryPath,
                TryReadDuration(libraryPath),
                ChooseText(metadata?.DisplayName, ClipLibraryService.CreateFallbackDisplayName(libraryPath)),
                ChooseText(metadata?.OriginalFileName, Path.GetFileName(libraryPath)))
            {
                IsLoopEnabled = metadata?.IsLoopEnabled == true,
                IsPinned = metadata?.IsPinned == true,
                HotkeyText = metadata?.HotkeyText ?? string.Empty
            });
            recoveredLibraryClip = true;
        }

        SortClipsForDisplay();
        return recoveredLibraryClip;
    }

    private static string ChooseText(string? preferred, string? fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback ?? string.Empty : preferred;
    }

    private static string ChooseDisplayName(string path, string? settingsDisplayName, string? metadataDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(metadataDisplayName) &&
            (string.IsNullOrWhiteSpace(settingsDisplayName) ||
             string.Equals(settingsDisplayName, ClipLibraryService.CreateFallbackDisplayName(path), StringComparison.Ordinal)))
        {
            return metadataDisplayName;
        }

        return ChooseText(settingsDisplayName, metadataDisplayName);
    }

    private static void SaveClipMetadata(SoundClip clip)
    {
        ClipLibraryService.SaveMetadata(
            clip.Path,
            clip.Name,
            clip.OriginalFileName,
            clip.IsLoopEnabled,
            clip.IsPinned,
            clip.HotkeyText);
    }

    private double GetSelectedDuckingAmountDb()
    {
        if (DuckingAmountCombo?.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), out var amountDb))
        {
            return amountDb;
        }

        return -12.0;
    }

    private MonitorMode GetSelectedMonitorMode()
    {
        if (MonitorModeMp3?.IsChecked == true)
        {
            return MonitorMode.Mp3Only;
        }

        if (MonitorModeMixed?.IsChecked == true)
        {
            return MonitorMode.Mixed;
        }

        return MonitorMode.None;
    }

    private void SelectMonitorMode(MonitorMode mode)
    {
        if (MonitorModeOff is null || MonitorModeMp3 is null || MonitorModeMixed is null)
        {
            return;
        }

        MonitorModeOff.IsChecked = mode == MonitorMode.None;
        MonitorModeMp3.IsChecked = mode == MonitorMode.Mp3Only;
        MonitorModeMixed.IsChecked = mode == MonitorMode.Mixed;
    }

    private static MonitorMode ParseMonitorMode(string? value)
    {
        return Enum.TryParse<MonitorMode>(value, ignoreCase: true, out var mode)
            ? mode
            : MonitorMode.None;
    }

    private void UpdateMonitorWarning()
    {
        if (MonitorWarningText is null)
        {
            return;
        }

        if (MonitorOutputText is not null)
        {
            MonitorOutputText.Text = (MonitorOutputCombo?.SelectedItem as AudioDeviceInfo)?.Name ?? "既定の再生デバイス";
        }

        var monitorMode = GetSelectedMonitorMode();
        var vcOutput = OutputDeviceCombo?.SelectedItem as AudioDeviceInfo;
        var monitorOutput = MonitorOutputCombo?.SelectedItem as AudioDeviceInfo;
        var sameDevice = monitorMode != MonitorMode.None &&
                         vcOutput is not null &&
                         monitorOutput is not null &&
                         vcOutput.DeviceNumber == monitorOutput.DeviceNumber;
        MonitorWarningText.Visibility = sameDevice ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        UpdateMeterLevels();
        foreach (var clip in Clips)
        {
            if (clip.PlaybackId is Guid playbackId && !_engine.IsClipPlaying(playbackId))
            {
                clip.PlaybackId = null;
                clip.IsPlaying = false;
                clip.Position = TimeSpan.Zero;
                continue;
            }

            if (clip.PlaybackId is Guid activePlaybackId && clip.IsPlaying && !clip.IsSeeking)
            {
                clip.Position = _engine.GetClipPosition(activePlaybackId);
            }
        }

        UpdateActiveClipDisplay();
    }

    private void UpdateMeterLevels()
    {
        var levels = _engine.GetLevels();
        _displayedMicLevel = SmoothLevel(ApplyMeterSensitivity(levels.Mic, _settings.MicMeterSensitivity), _displayedMicLevel);
        _displayedClipsLevel = SmoothLevel(ApplyMeterSensitivity(levels.Clips, _settings.ClipsMeterSensitivity), _displayedClipsLevel);
        _displayedOutputLevel = SmoothLevel(ApplyMeterSensitivity(levels.Output, _settings.OutputMeterSensitivity), _displayedOutputLevel);
        _viewModel.SetLevels(_displayedMicLevel, _displayedClipsLevel, _displayedOutputLevel);
    }

    private static double ApplyMeterSensitivity(double level, double sensitivity)
    {
        if (double.IsNaN(sensitivity) || double.IsInfinity(sensitivity) || sensitivity <= 0)
        {
            sensitivity = 1.0;
        }

        return Math.Clamp(level * Math.Clamp(sensitivity, 0.5, 4.0), 0.0, 1.0);
    }

    private static double SmoothLevel(double rawLevel, double displayedLevel)
    {
        return Math.Max(rawLevel, displayedLevel * 0.85);
    }

    private void Engine_ClipPlaybackEnded(object? sender, ClipPlaybackEndedEventArgs e)
    {
        Dispatcher.Invoke(() => CleanupPlayback(e.PlaybackId));
    }

    private void CleanupPlayback(Guid playbackId)
    {
        var clip = Clips.FirstOrDefault(candidate => candidate.PlaybackId == playbackId);
        if (clip is null)
        {
            return;
        }

        clip.PlaybackId = null;
        clip.IsPlaying = false;
        clip.Position = TimeSpan.Zero;
        UpdateActiveClipDisplay();
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundClip clip)
        {
            clip.IsSeeking = true;
        }
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundClip clip)
        {
            return;
        }

        clip.IsSeeking = false;
        if (clip.PlaybackId is Guid playbackId)
        {
            _engine.SeekClip(playbackId, clip.Position);
        }
    }

    private void ClipScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void ClipCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundClip clip)
        {
            _selectedClip = clip;
            UpdateSelectedClipToolbar();
        }
    }

    private void PinClip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundClip clip)
        {
            return;
        }

        clip.IsPinned = !clip.IsPinned;
        _selectedClip = clip;
        SortClipsForDisplay();
        RefreshClipSections();
        UpdateSelectedClipToolbar();
        SaveSettings();
        e.Handled = true;
    }

    private void SortClipsForDisplay()
    {
        var sorted = Clips
            .OrderByDescending(clip => clip.IsPinned)
            .ThenBy(clip => Clips.IndexOf(clip))
            .ToList();

        for (var index = 0; index < sorted.Count; index++)
        {
            var currentIndex = Clips.IndexOf(sorted[index]);
            if (currentIndex != index)
            {
                Clips.Move(currentIndex, index);
            }
        }
    }

    private void RefreshClipSections()
    {
        var query = GetClipSearchQuery();
        _viewModel.DisplayedClips.Clear();

        foreach (var clip in Clips.Where(clip => MatchesClipSearch(clip, query)))
        {
            _viewModel.DisplayedClips.Add(clip);
        }
    }

    private string GetClipSearchQuery()
    {
        if (SearchBox is null)
        {
            return string.Empty;
        }

        var text = SearchBox.Text.Trim();
        return string.Equals(text, SearchPlaceholder, StringComparison.Ordinal)
            ? string.Empty
            : text;
    }

    private static bool MatchesClipSearch(SoundClip clip, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return clip.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               clip.OriginalFileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               clip.FileName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSelectedClipToolbar()
    {
        if (SelectedClipNameText is null ||
            RenameClipButton is null ||
            HotkeyClipButton is null ||
            RemoveClipButton is null ||
            LoopCheck is null)
        {
            return;
        }

        var hasSelection = _selectedClip is not null;
        _isUpdatingSelectedClipToolbar = true;
        SelectedClipNameText.Text = hasSelection
            ? _selectedClip!.Name
            : "クリップを選択すると編集できます";
        SelectedClipNameText.Foreground = (Brush)FindResource(hasSelection ? "PrimaryTextBrush" : "MutedTextBrush");
        RenameClipButton.IsEnabled = hasSelection;
        HotkeyClipButton.IsEnabled = hasSelection;
        HotkeyClipButton.ToolTip = hasSelection
            ? $"キー設定: {_selectedClip!.HotkeyDisplayText}"
            : "キー設定";
        RemoveClipButton.IsEnabled = hasSelection;
        LoopCheck.IsChecked = hasSelection && _selectedClip!.IsLoopEnabled;
        LoopCheck.IsEnabled = hasSelection;
        _isUpdatingSelectedClipToolbar = false;
    }

    private void UpdateRouteDisplay()
    {
        var inputName = (InputDeviceCombo?.SelectedItem as AudioDeviceInfo)?.Name ?? "Mic";
        var outputName = (OutputDeviceCombo?.SelectedItem as AudioDeviceInfo)?.Name ?? "VC Output";
        var route = $"{Shorten(inputName, 24)} -> {Shorten(outputName, 30)}";

        if (CurrentRouteText is not null)
        {
            CurrentRouteText.Text = route;
        }

        if (RouteSummaryText is not null)
        {
            RouteSummaryText.Text = $"ルート: {route}";
        }
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 1)] + "...";
    }

    private void UpdateActiveClipDisplay()
    {
        if (ActiveClipsText is null)
        {
            return;
        }

        var activeCount = Clips.Count(clip => clip.IsPlaying);
        ActiveClipsText.Text = $"{activeCount} 件再生中";

        ActiveClips.Clear();
        foreach (var clip in Clips.Where(clip => clip.IsPlaying))
        {
            ActiveClips.Add(clip);
        }

        UpdateClipEmptyState();
    }

    private void UpdateClipEmptyState()
    {
        if (ClipScrollViewer is null || MockClipGrid is null)
        {
            return;
        }

        var hasClips = Clips.Count > 0;
        ClipScrollViewer.Visibility = hasClips ? Visibility.Visible : Visibility.Collapsed;
        MockClipGrid.Visibility = hasClips ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStatus(string status, string topStatus)
    {
        StatusText.Text = status;
        TopStatusText.Text = topStatus;
        TopStatusDot.Fill = topStatus switch
        {
            "Pipe Running" => (Brush)FindResource("SuccessBrush"),
            "Error" => (Brush)FindResource("ErrorBrush"),
            _ => (Brush)FindResource("AccentBlueBrush")
        };
        TopStatusText.Foreground = topStatus switch
        {
            "Pipe Running" => (Brush)FindResource("SuccessBrush"),
            "Error" => (Brush)FindResource("ErrorBrush"),
            _ => (Brush)FindResource("AccentBlueBrush")
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _uiTimer.Stop();
        _engine.ClipPlaybackEnded -= Engine_ClipPlaybackEnded;
        _windowSource?.RemoveHook(WindowMessageHook);
        _hotkeys.Dispose();
        SaveSettings();
        _engine.Dispose();
        base.OnClosed(e);
    }
}
