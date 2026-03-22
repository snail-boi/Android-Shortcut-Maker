using System.Text.RegularExpressions;
using System.Windows;

namespace android_shortcut_maker;

public partial class SettingsWindow : Window
{
    private readonly ShortcutMakerConfig _config;

    public SettingsWindow(ShortcutMakerConfig config)
    {
        InitializeComponent();
        _config = config;

        TxtName.Text = _config.SelectedDeviceName;
        TxtUsb.Text = _config.SelectedDeviceUSB;
        TxtWifi.Text = _config.SelectedDeviceWiFi;
    }

    private async void BtnAuto_Click(object sender, RoutedEventArgs e)
    {
        BtnAuto.IsEnabled = false;
        TxtStatus.Text = "Looking for USB device...";

        try
        {
            var adb = _config.Paths.Adb;
            var devices = await AdbRunner.RunCaptureAsync(adb, "devices");
            var lines = devices.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var usb = lines
                .Where(x => x.EndsWith("device", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Split('\t', ' ').FirstOrDefault())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.Contains(':'));

            if (string.IsNullOrWhiteSpace(usb))
            {
                TxtStatus.Text = "No USB device found.";
                return;
            }

            TxtUsb.Text = usb;

            var portOutput = await AdbRunner.RunCaptureAsync(adb, $"-s {usb} shell getprop service.adb.tcp.port");
            var port = int.TryParse(portOutput.Trim(), out var parsedPort) && parsedPort > 0 ? parsedPort : 5555;

            var ipOutput = await AdbRunner.RunCaptureAsync(adb, $"-s {usb} shell ip -f inet addr show wlan0");
            var match = Regex.Match(ipOutput, @"inet\s+(?<ip>\d+\.\d+\.\d+\.\d+)");
            if (!match.Success)
            {
                var routeOutput = await AdbRunner.RunCaptureAsync(adb, $"-s {usb} shell ip route");
                match = Regex.Match(routeOutput, @"src\s+(?<ip>\d+\.\d+\.\d+\.\d+)");
            }

            if (match.Success)
            {
                TxtWifi.Text = string.Concat(match.Groups["ip"].Value, ":", port);
            }

            var nameDialog = new InputDialog("Device Name", "Enter a name for this device:", TxtName.Text)
            {
                Owner = this
            };

            if (nameDialog.ShowDialog() == true)
            {
                TxtName.Text = nameDialog.InputText;
            }

            TxtStatus.Text = "Device info auto-detected.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Auto detect failed: " + ex.Message;
        }
        finally
        {
            BtnAuto.IsEnabled = true;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var name = (TxtName.Text ?? string.Empty).Trim();
        var usb = (TxtUsb.Text ?? string.Empty).Trim();
        var wifi = (TxtWifi.Text ?? string.Empty).Trim();

        _config.SelectedDeviceName = name;
        _config.SelectedDeviceUSB = usb;
        _config.SelectedDeviceWiFi = wifi;

        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(usb) || !string.IsNullOrWhiteSpace(wifi))
        {
            var existing = _config.SavedDevices.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(name) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(usb) && string.Equals(x.UsbSerial, usb, StringComparison.OrdinalIgnoreCase)));

            if (existing == null)
            {
                _config.SavedDevices.Add(new SavedDevice
                {
                    Name = name,
                    UsbSerial = usb,
                    WifiIpPort = wifi
                });
            }
            else
            {
                existing.Name = name;
                existing.UsbSerial = usb;
                existing.WifiIpPort = wifi;
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
