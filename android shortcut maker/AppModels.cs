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

    /// <summary>
    /// Persisted mDNS service name (e.g. "adb-XXXXXXXX-XXXXXX") assigned during
    /// Wireless Debugging pairing. Stable across reboots and IP changes.
    /// </summary>
    public string WifiMdnsServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Last successfully connected ip:port for this device's WD session.
    /// Used as a best-effort fallback when mDNS discovery fails (e.g. mDNS
    /// blocked by the LAN). Never relied on as the primary connection method.
    /// </summary>
    public string WifiLastKnownIpPort { get; set; } = string.Empty;

    // ---- migration support ----
    // The old config stored a raw ip:port in WifiIpPort. On first load this
    // field is carried over to WifiLastKnownIpPort by AppConfig.Normalize().
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? WifiIpPort { get; set; }
}