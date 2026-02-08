using System;
using System.IO;
using System.Text.Json;

namespace windows_phone_app_shortcut
{
    internal sealed class ShortcutConfig
    {
        public ShortcutPaths Paths { get; set; } = new ShortcutPaths();
        public string SelectedDeviceUSB { get; set; } = string.Empty;
        public string SelectedDeviceWiFi { get; set; } = string.Empty;
        public string SelectedDeviceName { get; set; } = string.Empty;
        public List<ShortcutDeviceConfig> SavedDevices { get; set; } = new List<ShortcutDeviceConfig>();
    }

    internal sealed class ShortcutDeviceConfig
    {
        public string Name { get; set; } = string.Empty;
        public string UsbSerial { get; set; } = string.Empty;
        public string TcpIp { get; set; } = string.Empty;
    }

    internal sealed class ShortcutPaths
    {
        public string Adb { get; set; } = string.Empty;
        public string Scrcpy { get; set; } = string.Empty;
    }

    internal static class ShortcutConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static ShortcutConfig Load()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var primaryPath = Path.Combine(appData, "Snail", "Config.json");
            var fallbackPath = Path.Combine(appData, "Snail", "config.json");
            var configPath = File.Exists(primaryPath) ? primaryPath : File.Exists(fallbackPath) ? fallbackPath : null;

            if (string.IsNullOrEmpty(configPath))
            {
                return new ShortcutConfig();
            }

            try
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<ShortcutConfig>(json, JsonOptions) ?? new ShortcutConfig();
            }
            catch
            {
                return new ShortcutConfig();
            }
        }

        public static string ResolveAdbPath(ShortcutConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config?.Paths?.Adb) && File.Exists(config.Paths.Adb))
            {
                return config.Paths.Adb;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "adb.exe");
        }

        public static string ResolveScrcpyPath(ShortcutConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config?.Paths?.Scrcpy) && File.Exists(config.Paths.Scrcpy))
            {
                return config.Paths.Scrcpy;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "scrcpy.exe");
        }
    }
}
