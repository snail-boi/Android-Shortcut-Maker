using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace android_shortcut_maker;

public partial class SettingsWindow : Window
{
    private readonly ShortcutMakerConfig _config;

    // Global shortcut VK state (mirrors _config.GlobalShortcuts while editing).
    private int _gMod = 0x0001;
    private int _gAudioToggle = 0;
    private int _gPlayPause = 0;
    private int _gNext = 0;
    private int _gPrev = 0;
    private int _gVolUp = 0;
    private int _gVolDown = 0;
    private int _gQuality = 0;

    public SettingsWindow(ShortcutMakerConfig config)
    {
        InitializeComponent();
        _config = config;
        PopulateFields();
    }

    private void PopulateFields()
    {
        TxtName.Text = _config.SelectedDeviceName;
        TxtUsb.Text = _config.SelectedDeviceUSB;
        TxtMdns.Text = _config.SelectedDeviceWifiMdnsName;
        TxtLastIp.Text = _config.SelectedDeviceWifiLastKnownIpPort;

        BtnPairWireless.Content = string.IsNullOrWhiteSpace(_config.SelectedDeviceWifiMdnsName)
            ? "Pair phone..." : "Re-pair phone...";

        var gs = _config.GlobalShortcuts ?? new GlobalShortcutConfig();
        _gMod = gs.Modifier;
        _gAudioToggle = gs.AudioToggle;
        _gPlayPause = gs.PlayPause;
        _gNext = gs.Next;
        _gPrev = gs.Previous;
        _gVolUp = gs.VolumeUp;
        _gVolDown = gs.VolumeDown;
        _gQuality = gs.Quality;

        SyncModifierCombo();
        TxtGlobalAudioToggle.Text = VkHelper.ToDisplayName(_gAudioToggle);
        TxtGlobalPlayPause.Text = VkHelper.ToDisplayName(_gPlayPause);
        TxtGlobalNext.Text = VkHelper.ToDisplayName(_gNext);
        TxtGlobalPrev.Text = VkHelper.ToDisplayName(_gPrev);
        TxtGlobalVolUp.Text = VkHelper.ToDisplayName(_gVolUp);
        TxtGlobalVolDown.Text = VkHelper.ToDisplayName(_gVolDown);
        TxtGlobalQuality.Text = VkHelper.ToDisplayName(_gQuality);
    }

    private void SyncModifierCombo()
    {
        foreach (ComboBoxItem item in CmbGlobalMod.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var v) && v == _gMod)
            {
                CmbGlobalMod.SelectedItem = item;
                return;
            }
        }
        CmbGlobalMod.SelectedIndex = 1;
    }

    // ── Detect USB ─────────────────────────────────────────────────────────

    private async void BtnDetectUsb_Click(object sender, RoutedEventArgs e)
    {
        BtnDetectUsb.IsEnabled = false;
        TxtStatus.Text = "Looking for USB device...";
        try
        {
            AdbHelper.AdbPath = _config.Paths.Adb;
            if (!File.Exists(_config.Paths.Adb)) { TxtStatus.Text = "adb.exe not found."; return; }

            var serial = await DetectUsbSerialAsync();
            if (string.IsNullOrWhiteSpace(serial)) { TxtStatus.Text = "No USB device found."; return; }

            TxtUsb.Text = serial;
            TxtStatus.Text = $"USB device detected: {serial}";
        }
        catch (Exception ex) { TxtStatus.Text = "Detection failed: " + ex.Message; }
        finally { BtnDetectUsb.IsEnabled = true; }
    }

    private static async Task<string> DetectUsbSerialAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var output = await AdbHelper.RunAdbCaptureAsync("devices");
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (!line.EndsWith("device", StringComparison.OrdinalIgnoreCase)) continue;
                var serial = line.Split('\t', ' ').FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(serial) && !serial.Contains(':')) return serial;
            }
            if (attempt < 5) await Task.Delay(400);
        }
        return string.Empty;
    }

    // ── Wireless Debugging pairing ──────────────────────────────────────────

    private async void BtnPairWireless_Click(object sender, RoutedEventArgs e)
    {
        AdbHelper.AdbPath = _config.Paths.Adb;
        if (!File.Exists(_config.Paths.Adb)) { TxtStatus.Text = "adb.exe not found."; return; }

        var dlg = new WifiPairDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        TxtStatus.Text = "Pairing succeeded. Discovering device...";
        string ipPort = string.Empty;
        if (!string.IsNullOrWhiteSpace(dlg.ServiceName))
            ipPort = await WirelessDebuggingHelper.ReconnectViaMdnsAsync(dlg.ServiceName);

        if (!string.IsNullOrWhiteSpace(dlg.ServiceName))
        {
            TxtMdns.Text = dlg.ServiceName;
            BtnPairWireless.Content = "Re-pair phone...";
        }

        TxtStatus.Text = !string.IsNullOrWhiteSpace(ipPort)
            ? $"Paired and connected at {ipPort}."
            : "Paired. Could not auto-discover ip:port via mDNS. Keep Wireless Debugging enabled.";

        if (!string.IsNullOrWhiteSpace(ipPort))
        {
            TxtLastIp.Text = ipPort;
            if (string.IsNullOrWhiteSpace(TxtUsb.Text))
            {
                var serial = (await AdbHelper.RunAdbCaptureAsync($"-s {ipPort} shell getprop ro.serialno")).Trim();
                if (string.IsNullOrWhiteSpace(serial))
                    serial = (await AdbHelper.RunAdbCaptureAsync($"-s {ipPort} shell getprop ro.boot.serialno")).Trim();
                if (!string.IsNullOrWhiteSpace(serial)) TxtUsb.Text = serial;
            }
        }
    }

    // ── Global shortcut pickers ─────────────────────────────────────────────

    private void BtnGlobalPickAudioToggle_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gAudioToggle, TxtGlobalAudioToggle);

    private void BtnGlobalPickPlayPause_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gPlayPause, TxtGlobalPlayPause);

    private void BtnGlobalPickNext_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gNext, TxtGlobalNext);

    private void BtnGlobalPickPrev_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gPrev, TxtGlobalPrev);

    private void BtnGlobalPickVolUp_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gVolUp, TxtGlobalVolUp);

    private void BtnGlobalPickVolDown_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gVolDown, TxtGlobalVolDown);

    private void BtnGlobalPickQuality_Click(object sender, RoutedEventArgs e)
        => PickKey(ref _gQuality, TxtGlobalQuality);

    private void PickKey(ref int field, System.Windows.Controls.TextBox display)
    {
        var picker = new HotkeyPicker { Owner = this };
        if (picker.ShowDialog() != true) return;
        field = picker.VirtualKey;
        display.Text = VkHelper.ToDisplayName(field);
    }

    // ── Save / Cancel ───────────────────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var name = (TxtName.Text ?? string.Empty).Trim();
        var usb = (TxtUsb.Text ?? string.Empty).Trim();
        var mdns = (TxtMdns.Text ?? string.Empty).Trim();
        var lastIp = (TxtLastIp.Text ?? string.Empty).Trim();

        _config.SelectedDeviceName = name;
        _config.SelectedDeviceUSB = usb;
        _config.SelectedDeviceWifiMdnsName = mdns;
        _config.SelectedDeviceWifiLastKnownIpPort = lastIp;

        if (CmbGlobalMod.SelectedItem is ComboBoxItem modItem
            && modItem.Tag is string modTag
            && int.TryParse(modTag, out var modVal))
            _gMod = modVal;

        _config.GlobalShortcuts = new GlobalShortcutConfig
        {
            Modifier = _gMod,
            AudioToggle = _gAudioToggle,
            PlayPause = _gPlayPause,
            Next = _gNext,
            Previous = _gPrev,
            VolumeUp = _gVolUp,
            VolumeDown = _gVolDown,
            Quality = _gQuality,
        };

        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(usb) || !string.IsNullOrWhiteSpace(mdns))
        {
            var existing = _config.SavedDevices.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(name) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(usb) && string.Equals(x.UsbSerial, usb, StringComparison.OrdinalIgnoreCase)));

            if (existing == null)
                _config.SavedDevices.Add(new SavedDevice { Name = name, UsbSerial = usb, WifiMdnsServiceName = mdns, WifiLastKnownIpPort = lastIp });
            else
            {
                existing.Name = name; existing.UsbSerial = usb;
                existing.WifiMdnsServiceName = mdns; existing.WifiLastKnownIpPort = lastIp;
            }
        }

        ShortcutMakerConfigStore.Save(_config);
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}