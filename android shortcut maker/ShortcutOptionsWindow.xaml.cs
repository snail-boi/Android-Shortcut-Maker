using System.Windows;
using System.Windows.Controls;

namespace android_shortcut_maker;

public partial class ShortcutOptionsWindow : Window
{
    private readonly AppInfo _app;

    public List<string> OptionArgs { get; } = new();

    public ShortcutOptionsWindow(AppInfo app)
    {
        InitializeComponent();
        _app = app;

        ChkAudioBuffer.Checked += (_, _) => UpdateLinkedInputVisibility();
        ChkAudioBuffer.Unchecked += (_, _) => UpdateLinkedInputVisibility();
        ChkVideoBuffer.Checked += (_, _) => UpdateLinkedInputVisibility();
        ChkVideoBuffer.Unchecked += (_, _) => UpdateLinkedInputVisibility();
        ChkMaxSize.Checked += (_, _) => UpdateLinkedInputVisibility();
        ChkMaxSize.Unchecked += (_, _) => UpdateLinkedInputVisibility();

        if (_app.Kind == ShortcutKind.AudioLink)
        {
            CommonOptionsPanel.Visibility = Visibility.Collapsed;
            AudioLinkPanel.Visibility = Visibility.Visible;
        }
        else
        {
            CommonOptionsPanel.Visibility = Visibility.Visible;
            AudioLinkPanel.Visibility = Visibility.Collapsed;
        }

        UpdateLinkedInputVisibility();
    }

    private void UpdateLinkedInputVisibility()
    {
        TxtAudioBuffer.Visibility = ChkAudioBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtAudioBufferUnit.Visibility = ChkAudioBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        TxtVideoBuffer.Visibility = ChkVideoBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtVideoBufferUnit.Visibility = ChkVideoBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        TxtMaxSize.Visibility = ChkMaxSize.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        OptionArgs.Clear();

        if (_app.Kind == ShortcutKind.AudioLink)
        {
            OptionArgs.Add("--no-video");
            OptionArgs.Add("--no-window");
            OptionArgs.Add("--audio-source=playback");

            var codec = (CmbCodec.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "raw";
            if (!codec.Equals("raw", StringComparison.OrdinalIgnoreCase))
            {
                OptionArgs.Add($"--audio-codec={codec}");
            }

            if (!codec.Equals("raw", StringComparison.OrdinalIgnoreCase)
                && !codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(TxtBitrate.Text, out var bitrate)
                && bitrate > 0)
            {
                OptionArgs.Add($"--audio-bit-rate={bitrate}K");
            }

            if (int.TryParse(TxtAudioLinkBuffer.Text, out var audioLinkBuffer) && audioLinkBuffer > 0)
            {
                OptionArgs.Add($"--audio-buffer={audioLinkBuffer}");
            }

            if (codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(TxtFlacLevel.Text, out var flacLevel)
                && flacLevel > 0)
            {
                OptionArgs.Add($"--audio-codec-options=flac-compression-level={Math.Clamp(flacLevel, 1, 8)}");
            }
        }
        else
        {
            if (ChkNoAudio.IsChecked == true) OptionArgs.Add("--no-audio");
            if (ChkPlaybackAudio.IsChecked == true) OptionArgs.Add("--audio-source=playback");
            if (ChkStayAwake.IsChecked == true) OptionArgs.Add("--stay-awake");
            if (ChkTurnScreenOff.IsChecked == true) OptionArgs.Add("--turn-screen-off");
            if (ChkLockAfterExit.IsChecked == true) OptionArgs.Add("--power-off-on-close");
            if (ChkTop.IsChecked == true) OptionArgs.Add("--always-on-top");

            if (ChkAudioBuffer.IsChecked == true && int.TryParse(TxtAudioBuffer.Text, out var audioBuffer) && audioBuffer > 0)
            {
                OptionArgs.Add($"--audio-buffer={audioBuffer}");
            }

            if (ChkVideoBuffer.IsChecked == true && int.TryParse(TxtVideoBuffer.Text, out var videoBuffer) && videoBuffer > 0)
            {
                OptionArgs.Add($"--video-buffer={videoBuffer}");
            }

            if (ChkMaxSize.IsChecked == true && int.TryParse(TxtMaxSize.Text, out var maxSize) && maxSize > 0)
            {
                OptionArgs.Add($"--max-size={maxSize}");
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
