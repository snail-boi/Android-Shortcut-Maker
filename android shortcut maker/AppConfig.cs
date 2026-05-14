using System.IO;
using System.Text.Json;

namespace android_shortcut_maker;

public sealed class ShortcutMakerConfig
{
    public ShortcutMakerPaths Paths { get; set; } = new();
    public string SelectedDeviceName { get; set; } = string.Empty;
    public string SelectedDeviceUSB { get; set; } = string.Empty;
    public string SelectedDeviceWifiMdnsName { get; set; } = string.Empty;
    public string SelectedDeviceWifiLastKnownIpPort { get; set; } = string.Empty;
    public List<SavedDevice> SavedDevices { get; set; } = new();
    public bool UseDarkMode { get; set; } = false;
    public GlobalShortcutConfig GlobalShortcuts { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? SelectedDeviceWiFi { get; set; }
}

public sealed class GlobalShortcutConfig
{
    public int Modifier { get; set; } = 0x0001;
    public int AudioToggle { get; set; } = 0;
    public int PlayPause { get; set; } = 0;
    public int Next { get; set; } = 0;
    public int Previous { get; set; } = 0;
    public int VolumeUp { get; set; } = 0;
    public int VolumeDown { get; set; } = 0;
    public int Quality { get; set; } = 0;
}

public sealed class ShortcutMakerPaths
{
    public string Adb { get; set; } = AppPaths.AdbPath;
    public string Scrcpy { get; set; } = AppPaths.ScrcpyPath;
}

public static class ShortcutMakerConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigPath => AppPaths.ConfigPath;

    public static ShortcutMakerConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<ShortcutMakerConfig>(json, JsonOptions);
                return Normalize(config ?? new ShortcutMakerConfig());
            }
        }
        catch { }

        return Normalize(new ShortcutMakerConfig());
    }

    public static void Save(ShortcutMakerConfig config)
    {
        var normalized = Normalize(config);
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public static ShortcutMakerConfig Normalize(ShortcutMakerConfig config)
    {
        config.Paths ??= new ShortcutMakerPaths();
        config.SavedDevices ??= new List<SavedDevice>();
        config.GlobalShortcuts ??= new GlobalShortcutConfig();

        if (!string.IsNullOrWhiteSpace(config.SelectedDeviceWiFi)
            && string.IsNullOrWhiteSpace(config.SelectedDeviceWifiLastKnownIpPort))
        {
            config.SelectedDeviceWifiLastKnownIpPort = config.SelectedDeviceWiFi;
        }
        config.SelectedDeviceWiFi = null;

        foreach (var device in config.SavedDevices)
        {
            if (!string.IsNullOrWhiteSpace(device.WifiIpPort)
                && string.IsNullOrWhiteSpace(device.WifiLastKnownIpPort))
            {
                device.WifiLastKnownIpPort = device.WifiIpPort;
            }
            device.WifiIpPort = null;
        }

        config.SavedDevices = config.SavedDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.Name)
                        || !string.IsNullOrWhiteSpace(d.UsbSerial)
                        || !string.IsNullOrWhiteSpace(d.WifiMdnsServiceName))
            .GroupBy(d => d.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new SavedDevice
                {
                    Name = (first.Name ?? string.Empty).Trim(),
                    UsbSerial = (first.UsbSerial ?? string.Empty).Trim(),
                    WifiMdnsServiceName = (first.WifiMdnsServiceName ?? string.Empty).Trim(),
                    WifiLastKnownIpPort = (first.WifiLastKnownIpPort ?? string.Empty).Trim()
                };
            })
            .ToList();

        return config;
    }
}