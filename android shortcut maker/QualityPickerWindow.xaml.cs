using System.Windows;
using System.Windows.Controls;

namespace android_shortcut_maker;

public partial class QualityPickerWindow : Window
{
    private readonly string[] _originalArgs;

    /// <summary>The full original arg array with audio args replaced by the chosen preset.</summary>
    public string[] UpdatedArgs { get; private set; }

    public QualityPickerWindow(string[] originalArgs)
    {
        InitializeComponent();
        _originalArgs = originalArgs;
        UpdatedArgs   = originalArgs;
        PopulatePresets();
    }

    private void PopulatePresets()
    {
        // Detect current codec/bitrate from args to pre-select the matching preset.
        var currentCodec  = ExtractArgValue(_originalArgs, "--audio-codec") ?? "raw";
        var currentBitrate = ExtractArgValue(_originalArgs, "--audio-bit-rate") ?? string.Empty;
        var currentBuffer = int.TryParse(ExtractArgValue(_originalArgs, "--audio-buffer"), out var buf) ? buf : 80;

        AudioQualityPresets.Preset? currentMatch = AudioQualityPresets.MatchFromArgs(currentCodec, currentBitrate, currentBuffer, 2);

        foreach (var preset in AudioQualityPresets.All)
        {
            var item = new ListBoxItem
            {
                Content = $"{preset.ShortName}  —  {preset.Description}",
                Tag     = preset
            };
            PresetList.Items.Add(item);

            if (currentMatch != null
                && string.Equals(preset.Name, currentMatch.Name, StringComparison.OrdinalIgnoreCase))
            {
                PresetList.SelectedItem = item;
            }
        }

        if (PresetList.SelectedItem == null && PresetList.Items.Count > 0)
            PresetList.SelectedIndex = 1; // Default
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not ListBoxItem { Tag: AudioQualityPresets.Preset preset })
            return;

        // Remove existing audio quality args from the original set.
        var stripped = _originalArgs
            .Where(a => !a.StartsWith("--audio-codec",     StringComparison.OrdinalIgnoreCase)
                     && !a.StartsWith("--audio-bit-rate",  StringComparison.OrdinalIgnoreCase)
                     && !a.StartsWith("--audio-buffer",    StringComparison.OrdinalIgnoreCase)
                     && !a.StartsWith("--audio-codec-options", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Insert new quality args.
        if (!preset.Codec.Equals("raw", StringComparison.OrdinalIgnoreCase))
            stripped.Add($"--audio-codec={preset.Codec}");

        if (!string.IsNullOrWhiteSpace(preset.Bitrate))
            stripped.Add($"--audio-bit-rate={preset.Bitrate}K");

        if (preset.BufferMs > 0)
            stripped.Add($"--audio-buffer={preset.BufferMs}");

        if (preset.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase))
            stripped.Add($"--audio-codec-options=flac-compression-level={preset.FlacCompressionLevel}");

        UpdatedArgs  = stripped.ToArray();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string? ExtractArgValue(string[] args, string key)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return arg[(key.Length + 1)..].TrimEnd('K', 'k');
        }
        return null;
    }
}
