using System.Collections.Generic;

namespace android_shortcut_maker;

/// <summary>
/// Virtual key code helpers. Extracted from AMPL's MainWindow_Hotkeys pattern
/// so both ShortcutOptionsWindow and ShortcutHost can share them.
/// </summary>
public static class VkHelper
{
    public static string ToDisplayName(int vk)
    {
        if (vk == 0) return "None";
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);

        var map = new Dictionary<int, string>
        {
            { 0xAF, "Vol+" },
            { 0xAE, "Vol-" },
            { 0xAD, "Mute" },
            { 0xB3, "Play/Pause" },
            { 0xB0, "Next" },
            { 0xB1, "Prev" },
            { 0xB2, "Stop" },
            { 0x1B, "Esc" },
            { 0x0D, "Enter" },
            { 0x20, "Space" },
            { 0x26, "Up" },
            { 0x28, "Down" },
            { 0x25, "Left" },
            { 0x27, "Right" },
        };

        return map.TryGetValue(vk, out var name) ? name : $"0x{vk:X2}";
    }

    public static string ModifierToDisplayName(int mod)
    {
        return mod switch
        {
            0x0001 => "Alt",
            0x0002 => "Ctrl",
            0x0004 => "Shift",
            0x0003 => "Ctrl+Alt",
            0x0005 => "Alt+Shift",
            0x0006 => "Ctrl+Shift",
            0x0007 => "Ctrl+Alt+Shift",
            _      => "None"
        };
    }
}
