using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace windows_phone_app_shortcut
{
    internal static class ShortcutScrcpyLauncher
    {
        public static async Task<bool> TryLaunchFromArgsAsync(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return false;
            }

            var config = ShortcutConfigLoader.Load();
            var adbPath = ShortcutConfigLoader.ResolveAdbPath(config);
            var scrcpyPath = ShortcutConfigLoader.ResolveScrcpyPath(config);

            if (!File.Exists(adbPath))
            {
                MessageBox.Show("adb.exe not found. Please configure Phone Utils first.", "Scarlet Phone Shortcuts", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }

            if (!File.Exists(scrcpyPath))
            {
                MessageBox.Show("scrcpy.exe not found. Please configure Phone Utils first.", "Scarlet Phone Shortcuts", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }

            var deviceName = ExtractDeviceName(args);
            var device = await ResolveDeviceAsync(adbPath, config, deviceName).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(device))
            {
                var message = string.IsNullOrWhiteSpace(deviceName)
                    ? "No configured device was detected. Check your USB/Wi-Fi settings in Phone Utils."
                    : $"Device '{deviceName}' is not nearby. Connect it via USB or Wi-Fi and try again.";
                MessageBox.Show(message, "Scarlet Phone Shortcuts", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            var sanitizedArgs = SanitizeScrcpyArgs(args);
            var psi = new ProcessStartInfo
            {
                FileName = scrcpyPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(device);
            foreach (var arg in sanitizedArgs)
            {
                psi.ArgumentList.Add(arg);
            }

            var process = Process.Start(psi);
            if (process == null)
            {
                MessageBox.Show("Failed to launch scrcpy.", "Scarlet Phone Shortcuts", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return true;
        }

        private static async Task<string?> ResolveDeviceAsync(string adbPath, ShortcutConfig config, string? deviceName)
        {
            var target = !string.IsNullOrWhiteSpace(deviceName)
                ? config.SavedDevices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                : null;

            var usbId = target?.UsbSerial ?? config.SelectedDeviceUSB;
            var wifiId = target?.TcpIp ?? config.SelectedDeviceWiFi;

            var devicesOutput = await ShortcutAdbHelper.RunAdbCaptureAsync(adbPath, "devices").ConfigureAwait(false);
            var deviceLines = SplitDeviceLines(devicesOutput);

            if (!string.IsNullOrWhiteSpace(usbId) && IsDeviceOnline(deviceLines, usbId))
            {
                return usbId;
            }

            if (!string.IsNullOrWhiteSpace(wifiId) && !wifiId.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsDeviceOnline(deviceLines, wifiId))
                {
                    await ShortcutAdbHelper.RunAdbCaptureAsync(adbPath, $"connect {wifiId}").ConfigureAwait(false);
                    devicesOutput = await ShortcutAdbHelper.RunAdbCaptureAsync(adbPath, "devices").ConfigureAwait(false);
                    deviceLines = SplitDeviceLines(devicesOutput);
                }

                if (IsDeviceOnline(deviceLines, wifiId))
                {
                    return wifiId;
                }
            }

            return null;
        }

        private static string? ExtractDeviceName(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Equals("--device-name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                if (arg.StartsWith("--device-name=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring("--device-name=".Length).Trim('"');
                }
            }

            return null;
        }

        private static string[] SplitDeviceLines(string output) => output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        private static bool IsDeviceOnline(IEnumerable<string> deviceLines, string deviceId) =>
            deviceLines.Any(line => line.StartsWith(deviceId, StringComparison.OrdinalIgnoreCase) && line.TrimEnd().EndsWith("device", StringComparison.OrdinalIgnoreCase));

        private static List<string> SanitizeScrcpyArgs(string[] args)
        {
            var sanitized = new List<string>();
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

                if (arg.Equals("--device-name", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                if (arg.StartsWith("--device-name=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sanitized.Add(arg);
            }

            return sanitized;
        }
    }

    internal static class ShortcutAdbHelper
    {
        public static async Task<string> RunAdbCaptureAsync(string adbPath, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return string.Empty;
            }

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(error))
            {
                output = string.Concat(output, Environment.NewLine, error).Trim();
            }

            return output ?? string.Empty;
        }
    }
}
