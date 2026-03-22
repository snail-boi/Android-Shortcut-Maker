using System.IO;
using System.Text.Json;

namespace android_shortcut_maker;

public sealed class ShortcutMakerConfig
{
    public ShortcutMakerPaths Paths { get; set; } = new();
    public string SelectedDeviceName { get; set; } = string.Empty;
    public string SelectedDeviceUSB { get; set; } = string.Empty;
    public string SelectedDeviceWiFi { get; set; } = string.Empty;
    public List<SavedDevice> SavedDevices { get; set; } = new();
    public bool UseDarkMode { get; set; } = false;
}

public sealed class ShortcutMakerPaths
{
    public string Adb { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Snail",
        "Resources",
        "adb.exe");

    public string Scrcpy { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Snail",
        "Resources",
        "scrcpy.exe");
}

public static class ShortcutMakerConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Snail",
        "AndroidShortcutMaker",
        "config.json");

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
        catch
        {
        }

        return Normalize(new ShortcutMakerConfig());
    }

    public static void Save(ShortcutMakerConfig config)
    {
        var normalized = Normalize(config);
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public static ShortcutMakerConfig Normalize(ShortcutMakerConfig config)
    {
        config.Paths ??= new ShortcutMakerPaths();
        config.SavedDevices ??= new List<SavedDevice>();

        config.SavedDevices = config.SavedDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.Name) || !string.IsNullOrWhiteSpace(d.UsbSerial) || !string.IsNullOrWhiteSpace(d.WifiIpPort))
            .GroupBy(d => d.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new SavedDevice
                {
                    Name = (first.Name ?? string.Empty).Trim(),
                    UsbSerial = (first.UsbSerial ?? string.Empty).Trim(),
                    WifiIpPort = (first.WifiIpPort ?? string.Empty).Trim()
                };
            })
            .ToList();

        return config;
    }
}
