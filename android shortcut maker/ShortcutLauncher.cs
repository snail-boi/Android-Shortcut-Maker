using System.Diagnostics;
using System.IO;
using System.Windows;

namespace android_shortcut_maker;

internal static class ShortcutLauncher
{
    public static bool ShouldStayResident { get; private set; }

    public static async Task<bool> TryLaunchFromShortcutAsync(string[] args)
    {
        Debugger.Show($"[Launch] Raw args ({args?.Length ?? 0}): {string.Join(" | ", args ?? [])}");

        if (args == null || args.Length == 0) return false;

        var config = ShortcutMakerConfigStore.Load();
        AdbHelper.AdbPath = config.Paths.Adb;

        if (!File.Exists(config.Paths.Adb))
        {
            MessageBox.Show("adb.exe not found. Configure paths in settings.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        }

        if (!File.Exists(config.Paths.Scrcpy))
        {
            MessageBox.Show("scrcpy.exe not found. Configure paths in settings.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        }

        var targetName = ExtractValue(args, "--target-name") ?? string.Empty;
        var targetUsb = ExtractValue(args, "--target-usb") ?? string.Empty;
        var targetMdns = ExtractValue(args, "--target-mdns") ?? string.Empty;
        var targetLastIp = ExtractValue(args, "--target-last-ip") ?? string.Empty;

        Debugger.Show($"[Launch] Extracted: name='{targetName}' usb='{targetUsb}' mdns='{targetMdns}' lastip='{targetLastIp}'");

        if (string.IsNullOrWhiteSpace(targetUsb) && string.IsNullOrWhiteSpace(targetMdns) && !string.IsNullOrWhiteSpace(targetName))
        {
            var saved = config.SavedDevices.FirstOrDefault(x =>
                string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
            {
                targetUsb = saved.UsbSerial;
                targetMdns = saved.WifiMdnsServiceName;
                targetLastIp = saved.WifiLastKnownIpPort;
            }
        }

        if (string.IsNullOrWhiteSpace(targetUsb)) targetUsb = config.SelectedDeviceUSB;
        if (string.IsNullOrWhiteSpace(targetMdns)) targetMdns = config.SelectedDeviceWifiMdnsName;
        if (string.IsNullOrWhiteSpace(targetLastIp)) targetLastIp = config.SelectedDeviceWifiLastKnownIpPort;

        var resolved = await ResolveDeviceAsync(targetUsb, targetMdns, targetLastIp);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            Debugger.Show("[Launch] No device found.");
            MessageBox.Show(
                "No device found. Connect via USB or enable Wireless Debugging on your phone.",
                "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        Debugger.Show($"[Launch] Resolved device: '{resolved}'");

        if (resolved.Contains(':') && !string.IsNullOrWhiteSpace(targetMdns))
        {
            config.SelectedDeviceWifiLastKnownIpPort = resolved;
            var saved = config.SavedDevices.FirstOrDefault(x =>
                string.Equals(x.WifiMdnsServiceName, targetMdns, StringComparison.OrdinalIgnoreCase));
            if (saved != null) saved.WifiLastKnownIpPort = resolved;
            ShortcutMakerConfigStore.Save(config);
        }

        var scEnabled = args.Any(a => a.Equals("--sc-enabled", StringComparison.OrdinalIgnoreCase));
        var caEnabled = args.Any(a => a.Equals("--ca-enabled", StringComparison.OrdinalIgnoreCase));

        var sanitized = SanitizeArgs(args);

        if (scEnabled)
        {
            // In SC mode the video scrcpy never handles audio.
            // SharedAudioSession launches its own separate audio-only process.
            sanitized.RemoveAll(a =>
                a.Equals("--no-audio", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--audio-source", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--audio-codec", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--audio-bit-rate", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--audio-buffer", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--audio-codec-options", StringComparison.OrdinalIgnoreCase));
            sanitized.Add("--no-audio");
        }

        Debugger.Show($"[Launch] Sanitized args: {string.Join(" | ", sanitized)}");

        var psi = new ProcessStartInfo
        {
            FileName = config.Paths.Scrcpy,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(resolved);
        foreach (var arg in sanitized)
            psi.ArgumentList.Add(arg);

        var scrcpyProcess = Process.Start(psi);
        Debugger.Show($"[Launch] scrcpy started, pid={scrcpyProcess?.Id}");

        ShouldStayResident = scEnabled || caEnabled;

        if (ShouldStayResident && scrcpyProcess != null)
        {
            var scId = ExtractValue(args, "--sc-id") ?? Guid.NewGuid().ToString("N")[..8];
            Debugger.Show($"[Launch] Non-simple mode. sc-id={scId} sc={scEnabled} ca={caEnabled}");

            ShortcutHost.Start(new ShortcutHostConfig
            {
                ScrcpyProcess = scrcpyProcess,
                ScId = scId,
                DeviceSerial = resolved,
                UsbSerial = targetUsb,
                MdnsServiceName = targetMdns,
                LastKnownIpPort = targetLastIp,
                ScrcpyPath = config.Paths.Scrcpy,
                AdbPath = config.Paths.Adb,
                OriginalArgs = args,
                ShortcutControlEnabled = scEnabled,
                ConnectionAwareEnabled = caEnabled,
                Modifier = ParseInt(ExtractValue(args, "--sc-mod"), 0x0001),
                VkAudioToggle = ParseInt(ExtractValue(args, "--sc-audio-toggle"), 0),
                VkPlayPause = ParseInt(ExtractValue(args, "--sc-play-pause"), 0),
                VkNext = ParseInt(ExtractValue(args, "--sc-next"), 0),
                VkPrev = ParseInt(ExtractValue(args, "--sc-prev"), 0),
                VkVolUp = ParseInt(ExtractValue(args, "--sc-vol-up"), 0),
                VkVolDown = ParseInt(ExtractValue(args, "--sc-vol-down"), 0),
                VkQuality = ParseInt(ExtractValue(args, "--sc-quality"), 0),
            });
        }

        return true;
    }

    public static async Task<string> ResolveDeviceAsync(
        string usbSerial,
        string wifiMdnsServiceName,
        string wifiLastKnownIpPort)
    {
        var deviceList = await AdbHelper.RunAdbCaptureAsync("devices").ConfigureAwait(false);
        Debugger.Show($"[Resolve] adb devices: {deviceList.Replace("\n", " / ").Replace("\r", "")}");
        var lines = ParseDeviceLines(deviceList);

        if (!string.IsNullOrWhiteSpace(usbSerial) && IsConnected(lines, usbSerial))
        {
            Debugger.Show($"[Resolve] USB connected: {usbSerial}");
            return usbSerial;
        }

        if (!string.IsNullOrWhiteSpace(wifiMdnsServiceName))
        {
            Debugger.Show($"[Resolve] Trying mDNS '{wifiMdnsServiceName}'.");
            var ipPort = await WirelessDebuggingHelper.ReconnectViaMdnsAsync(wifiMdnsServiceName).ConfigureAwait(false);
            Debugger.Show($"[Resolve] mDNS result: '{ipPort}'");
            if (!string.IsNullOrWhiteSpace(ipPort)) return ipPort;
        }

        if (!string.IsNullOrWhiteSpace(wifiLastKnownIpPort))
        {
            Debugger.Show($"[Resolve] Trying last-known '{wifiLastKnownIpPort}'.");
            var ok = await WirelessDebuggingHelper.TryConnectLastKnownAsync(wifiLastKnownIpPort).ConfigureAwait(false);
            Debugger.Show($"[Resolve] Last-known result: {ok}");
            if (ok) return wifiLastKnownIpPort;
        }

        Debugger.Show("[Resolve] No device found.");
        return string.Empty;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string[] ParseDeviceLines(string output) => output
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Where(x => !x.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static bool IsConnected(IEnumerable<string> lines, string serial) =>
        lines.Any(x =>
            x.StartsWith(serial, StringComparison.OrdinalIgnoreCase)
            && x.TrimEnd().EndsWith("device", StringComparison.OrdinalIgnoreCase));

    public static string? ExtractValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(key.Length + 1)..].Trim('"');
        }
        return null;
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var v) ? v : fallback;

    public static List<string> SanitizeArgsPublic(string[] args) => SanitizeArgs(args);

    private static List<string> SanitizeArgs(string[] args)
    {
        var removedPrefixes = new[]
        {
            "--target-name", "--target-usb", "--target-mdns", "--target-last-ip", "--target-wifi",
            "--sc-id", "--sc-enabled", "--sc-mod",
            "--sc-audio-toggle", "--sc-play-pause", "--sc-next", "--sc-prev",
            "--sc-vol-up", "--sc-vol-down", "--sc-quality",
            "--ca-enabled",
        };

        var clean = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("-s", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--serial", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (arg.StartsWith("--serial=", StringComparison.OrdinalIgnoreCase)) continue;

            if (removedPrefixes.Any(p =>
                    arg.Equals(p, StringComparison.OrdinalIgnoreCase)
                    || arg.StartsWith(p + "=", StringComparison.OrdinalIgnoreCase)))
                continue;

            clean.Add(arg);
        }

        return clean;
    }
}