using System.Diagnostics;

namespace android_shortcut_maker;

/// <summary>
/// All the information ShortcutHost needs to run, assembled by ShortcutLauncher
/// and passed in at startup.
/// </summary>
public sealed class ShortcutHostConfig
{
    public required Process ScrcpyProcess { get; set; }
    public required string ScId           { get; init; }
    public required string DeviceSerial   { get; set; }
    public required string UsbSerial      { get; init; }
    public required string MdnsServiceName { get; init; }
    public required string LastKnownIpPort { get; init; }
    public required string ScrcpyPath     { get; init; }
    public required string AdbPath        { get; init; }
    public required string[] OriginalArgs { get; set; }

    public bool ShortcutControlEnabled { get; init; }
    public bool ConnectionAwareEnabled { get; init; }

    // Hotkeys (virtual key codes).
    public int Modifier      { get; init; } = 0x0001;
    public int VkAudioToggle { get; init; }
    public int VkPlayPause   { get; init; }
    public int VkNext        { get; init; }
    public int VkPrev        { get; init; }
    public int VkVolUp       { get; init; }
    public int VkVolDown     { get; init; }
    public int VkQuality     { get; init; }
}
