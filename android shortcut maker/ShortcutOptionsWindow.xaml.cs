using System.Windows;
using System.Windows.Controls;

namespace android_shortcut_maker;

public partial class ShortcutOptionsWindow : Window
{
    private readonly AppInfo _app;
    private readonly GlobalShortcutConfig _globalShortcuts;

    // Per-shortcut VK state (only used when ChkCustomShortcuts is checked).
    private int _scMod = 0x0001; // MOD_ALT default
    private int _scAudioToggle = 0;
    private int _scPlayPause = 0;
    private int _scNext = 0;
    private int _scPrev = 0;
    private int _scVolUp = 0;
    private int _scVolDown = 0;
    private int _scQuality = 0;

    public List<string> OptionArgs { get; } = new();

    public ShortcutOptionsWindow(AppInfo app)
    {
        InitializeComponent();
        _app = app;

        var config = ShortcutMakerConfigStore.Load();
        _globalShortcuts = config.GlobalShortcuts ?? new GlobalShortcutConfig();

        // Buffer/size checkbox toggles.
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

        // Shortcut Control is not available for AudioLink shortcuts (no video session to control).
        if (_app.Kind == ShortcutKind.AudioLink)
            ShortcutControlPanel.Visibility = Visibility.Collapsed;

        UpdateLinkedInputVisibility();
        UpdateShortcutControlVisibility();
        UpdateCustomShortcutsVisibility();
        SyncModifierCombo();
    }

    // ── Visibility helpers ──────────────────────────────────────────────────

    private void UpdateLinkedInputVisibility()
    {
        TxtAudioBuffer.Visibility = ChkAudioBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtAudioBufferUnit.Visibility = ChkAudioBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtVideoBuffer.Visibility = ChkVideoBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtVideoBufferUnit.Visibility = ChkVideoBuffer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtMaxSize.Visibility = ChkMaxSize.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateShortcutControlVisibility()
    {
        ScDetails.Visibility = ChkShortcutControl.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCustomShortcutsVisibility()
    {
        var custom = ChkCustomShortcuts.IsChecked == true;
        CustomShortcutsGrid.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        TxtGlobalShortcutsNote.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncModifierCombo()
    {
        foreach (ComboBoxItem item in CmbScModifier.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var v) && v == _scMod)
            {
                CmbScModifier.SelectedItem = item;
                return;
            }
        }
        CmbScModifier.SelectedIndex = 1; // Alt
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private void ChkShortcutControl_Changed(object sender, RoutedEventArgs e)
        => UpdateShortcutControlVisibility();

    private void ChkCustomShortcuts_Changed(object sender, RoutedEventArgs e)
        => UpdateCustomShortcutsVisibility();

    private void CmbScModifier_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbScModifier.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && int.TryParse(tag, out var v))
        {
            _scMod = v;
        }
    }

    private void BtnScHelp_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "Shortcut Control keeps this app running in the background after the shortcut launches.\n\n"
            + "It provides:\n"
            + "  - Hotkeys to toggle audio, control playback, adjust volume\n"
            + "  - An on-the-fly quality picker triggered by a hotkey\n\n"
            + "When multiple shortcuts are running simultaneously each has its own independent controls.",
            "Shortcut Control", MessageBoxButton.OK, MessageBoxImage.Information);

    private void BtnCaHelp_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "Connection Aware Mode monitors your USB connection.\n\n"
            + "If you unplug USB while running, it automatically switches to Wireless Debugging. "
            + "If you plug USB back in, it switches back.\n\n"
            + "Requires the app to stay in the background.",
            "Connection Aware Mode", MessageBoxButton.OK, MessageBoxImage.Information);

    // ── Hotkey pickers ──────────────────────────────────────────────────────

    private void BtnPickAudioToggle_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scAudioToggle, TxtScAudioToggle);

    private void BtnPickPlayPause_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scPlayPause, TxtScPlayPause);

    private void BtnPickNext_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scNext, TxtScNext);

    private void BtnPickPrev_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scPrev, TxtScPrev);

    private void BtnPickVolUp_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scVolUp, TxtScVolUp);

    private void BtnPickVolDown_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scVolDown, TxtScVolDown);

    private void BtnPickQuality_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _scQuality, TxtScQuality);

    private void PickKey(ref int field, System.Windows.Controls.TextBox display)
    {
        var picker = new HotkeyPicker { Owner = this };
        if (picker.ShowDialog() != true) return;
        field = picker.VirtualKey;
        display.Text = VkHelper.ToDisplayName(field);
    }

    // ── OK / Cancel ─────────────────────────────────────────────────────────

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        OptionArgs.Clear();
        BuildScrcpyArgs();
        BuildShortcutControlArgs();
        BuildConnectionAwareArgs();
        DialogResult = true;
        Close();
    }

    private void BuildScrcpyArgs()
    {
        if (_app.Kind == ShortcutKind.AudioLink)
        {
            OptionArgs.Add("--no-video");
            OptionArgs.Add("--no-window");
            OptionArgs.Add("--audio-source=playback");

            var codec = (CmbCodec.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "raw";
            if (!codec.Equals("raw", StringComparison.OrdinalIgnoreCase))
                OptionArgs.Add($"--audio-codec={codec}");

            if (!codec.Equals("raw", StringComparison.OrdinalIgnoreCase)
                && !codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(TxtBitrate.Text, out var bitrate) && bitrate > 0)
                OptionArgs.Add($"--audio-bit-rate={bitrate}K");

            if (int.TryParse(TxtAudioLinkBuffer.Text, out var alBuf) && alBuf > 0)
                OptionArgs.Add($"--audio-buffer={alBuf}");

            if (codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(TxtFlacLevel.Text, out var fl) && fl > 0)
                OptionArgs.Add($"--audio-codec-options=flac-compression-level={Math.Clamp(fl, 1, 8)}");

            return;
        }

        if (ChkNoAudio.IsChecked == true) OptionArgs.Add("--no-audio");
        if (ChkPlaybackAudio.IsChecked == true) OptionArgs.Add("--audio-source=playback");
        if (ChkStayAwake.IsChecked == true) OptionArgs.Add("--stay-awake");
        if (ChkTurnScreenOff.IsChecked == true) OptionArgs.Add("--turn-screen-off");
        if (ChkLockAfterExit.IsChecked == true) OptionArgs.Add("--power-off-on-close");
        if (ChkTop.IsChecked == true) OptionArgs.Add("--always-on-top");

        if (ChkAudioBuffer.IsChecked == true
            && int.TryParse(TxtAudioBuffer.Text, out var ab) && ab > 0)
            OptionArgs.Add($"--audio-buffer={ab}");

        if (ChkVideoBuffer.IsChecked == true
            && int.TryParse(TxtVideoBuffer.Text, out var vb) && vb > 0)
            OptionArgs.Add($"--video-buffer={vb}");

        if (ChkMaxSize.IsChecked == true
            && int.TryParse(TxtMaxSize.Text, out var ms) && ms > 0)
            OptionArgs.Add($"--max-size={ms}");
    }

    private void BuildShortcutControlArgs()
    {
        if (ChkShortcutControl.IsChecked != true) return;

        OptionArgs.Add("--sc-enabled");

        // Resolve which hotkeys to embed: custom or global.
        int mod, audioToggle, playPause, next, prev, volUp, volDown, quality;

        if (ChkCustomShortcuts.IsChecked == true)
        {
            mod = _scMod;
            audioToggle = _scAudioToggle;
            playPause = _scPlayPause;
            next = _scNext;
            prev = _scPrev;
            volUp = _scVolUp;
            volDown = _scVolDown;
            quality = _scQuality;
        }
        else
        {
            mod = _globalShortcuts.Modifier;
            audioToggle = _globalShortcuts.AudioToggle;
            playPause = _globalShortcuts.PlayPause;
            next = _globalShortcuts.Next;
            prev = _globalShortcuts.Previous;
            volUp = _globalShortcuts.VolumeUp;
            volDown = _globalShortcuts.VolumeDown;
            quality = _globalShortcuts.Quality;
        }

        OptionArgs.Add($"--sc-mod={mod}");
        if (audioToggle != 0) OptionArgs.Add($"--sc-audio-toggle={audioToggle}");
        if (playPause != 0) OptionArgs.Add($"--sc-play-pause={playPause}");
        if (next != 0) OptionArgs.Add($"--sc-next={next}");
        if (prev != 0) OptionArgs.Add($"--sc-prev={prev}");
        if (volUp != 0) OptionArgs.Add($"--sc-vol-up={volUp}");
        if (volDown != 0) OptionArgs.Add($"--sc-vol-down={volDown}");
        if (quality != 0) OptionArgs.Add($"--sc-quality={quality}");
    }

    private void BuildConnectionAwareArgs()
    {
        if (ChkConnectionAware.IsChecked == true)
            OptionArgs.Add("--ca-enabled");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}