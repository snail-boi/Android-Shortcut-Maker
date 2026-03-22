using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace android_shortcut_maker;

internal static class ShortcutLauncher
{
    public static async Task<bool> TryLaunchFromShortcutAsync(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return false;
        }

        var config = ShortcutMakerConfigStore.Load();
        var adbPath = config.Paths.Adb;
        var scrcpyPath = config.Paths.Scrcpy;

        if (!File.Exists(adbPath))
        {
            MessageBox.Show("adb.exe not found. Configure paths in Android Shortcut Maker settings.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        }

        if (!File.Exists(scrcpyPath))
        {
            MessageBox.Show("scrcpy.exe not found. Configure paths in Android Shortcut Maker settings.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        }

        var targetName = ExtractValue(args, "--target-name") ?? string.Empty;
        var targetUsb = ExtractValue(args, "--target-usb") ?? string.Empty;
        var targetWifi = ExtractValue(args, "--target-wifi") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(targetUsb) && string.IsNullOrWhiteSpace(targetWifi) && !string.IsNullOrWhiteSpace(targetName))
        {
            var saved = config.SavedDevices.FirstOrDefault(x => string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
            {
                targetUsb = saved.UsbSerial;
                targetWifi = saved.WifiIpPort;
            }
        }

        if (string.IsNullOrWhiteSpace(targetUsb)) targetUsb = config.SelectedDeviceUSB;
        if (string.IsNullOrWhiteSpace(targetWifi)) targetWifi = config.SelectedDeviceWiFi;

        var resolved = await ResolveDeviceAsync(adbPath, targetUsb, targetWifi, allowPortRecoveryPrompt: true);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            MessageBox.Show("No configured device found. Connect via USB or Wi-Fi.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        var sanitized = SanitizeArgs(args);
        var psi = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(resolved);
        foreach (var arg in sanitized)
        {
            psi.ArgumentList.Add(arg);
        }

        Process.Start(psi);
        return true;
    }

    public static async Task<string?> ResolveDeviceAsync(string adbPath, string usbSerial, string wifiIpPort, bool allowPortRecoveryPrompt)
    {
        var devices = await AdbRunner.RunCaptureAsync(adbPath, "devices");
        var lines = SplitDeviceLines(devices);

        if (!string.IsNullOrWhiteSpace(usbSerial) && IsConnected(lines, usbSerial))
        {
            return usbSerial;
        }

        if (!string.IsNullOrWhiteSpace(wifiIpPort) && !wifiIpPort.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsConnected(lines, wifiIpPort))
            {
                await AdbRunner.RunCaptureAsync(adbPath, $"connect {wifiIpPort}");
                devices = await AdbRunner.RunCaptureAsync(adbPath, "devices");
                lines = SplitDeviceLines(devices);
            }

            if (IsConnected(lines, wifiIpPort))
            {
                return wifiIpPort;
            }

            if (allowPortRecoveryPrompt && !string.IsNullOrWhiteSpace(usbSerial))
            {
                var res = MessageBox.Show(
                    "Wi-Fi connection failed. Reconnect the device over USB to set up the port again?",
                    "android shortcut maker",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    if (!IsConnected(lines, usbSerial))
                    {
                        MessageBox.Show("USB device is not connected yet. Connect USB and try the shortcut again.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }

                    var recoveredWifi = await TryRecoverWifiPortWithUsbAsync(adbPath, usbSerial, wifiIpPort);
                    if (!string.IsNullOrWhiteSpace(recoveredWifi))
                    {
                        MessageBox.Show("Port set up again.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Information);
                        return recoveredWifi;
                    }
                }
            }
        }

        return null;
    }

    public static async Task<string?> TryRecoverWifiPortWithUsbAsync(string adbPath, string usbSerial, string fallbackWifi)
    {
        await AdbRunner.RunAsync(adbPath, $"-s {usbSerial} tcpip 5555");

        var ipOutput = await AdbRunner.RunCaptureAsync(adbPath, $"-s {usbSerial} shell ip -f inet addr show wlan0");
        var match = Regex.Match(ipOutput, @"inet\s+(?<ip>\d+\.\d+\.\d+\.\d+)");

        var wifi = !match.Success
            ? fallbackWifi
            : string.Concat(match.Groups["ip"].Value, ":5555");

        if (string.IsNullOrWhiteSpace(wifi))
        {
            return null;
        }

        await AdbRunner.RunCaptureAsync(adbPath, $"connect {wifi}");
        var devices = await AdbRunner.RunCaptureAsync(adbPath, "devices");
        var lines = SplitDeviceLines(devices);
        return IsConnected(lines, wifi) ? wifi : null;
    }

    private static string? ExtractValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(key.Length + 1)..].Trim('"');
            }
        }

        return null;
    }

    private static string[] SplitDeviceLines(string output) => output
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Where(x => !x.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static bool IsConnected(IEnumerable<string> lines, string serial) =>
        lines.Any(x => x.StartsWith(serial, StringComparison.OrdinalIgnoreCase) && x.TrimEnd().EndsWith("device", StringComparison.OrdinalIgnoreCase));

    private static List<string> SanitizeArgs(string[] args)
    {
        var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--target-name",
            "--target-usb",
            "--target-wifi"
        };

        var clean = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("-s", StringComparison.OrdinalIgnoreCase) || arg.Equals("--serial", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (arg.StartsWith("--serial=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (removedKeys.Contains(arg))
            {
                i++;
                continue;
            }

            if (removedKeys.Any(k => arg.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            clean.Add(arg);
        }

        return clean;
    }
}
