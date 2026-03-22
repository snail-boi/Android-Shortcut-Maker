namespace android_shortcut_maker;

public enum ShortcutKind
{
    InstalledApp,
    AudioLink,
    Screencast,
    FrontCamera,
    BackCamera
}

public sealed class AppInfo
{
    public required string PackageName { get; init; }
    public required string DisplayName { get; init; }
    public ShortcutKind Kind { get; init; } = ShortcutKind.InstalledApp;
    public string? IconPath { get; set; }
}

public sealed class SavedDevice
{
    public string Name { get; set; } = string.Empty;
    public string UsbSerial { get; set; } = string.Empty;
    public string WifiIpPort { get; set; } = string.Empty;
}
