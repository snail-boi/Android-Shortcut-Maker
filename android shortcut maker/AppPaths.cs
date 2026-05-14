using System.IO;

namespace android_shortcut_maker;

/// <summary>
/// Single source of truth for all data and resource paths.
/// In portable mode (portable.mode file exists next to the exe), everything
/// lives relative to the exe. Otherwise uses %AppData%\Snail\AndroidShortcutMaker.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<bool> _isPortable = new(() =>
    {
        var exeDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(exeDir, "portable.mode"));
    });

    public static bool IsPortable => _isPortable.Value;

    /// <summary>Root data directory. Config, logs, and icons live under here.</summary>
    public static string DataDir => IsPortable
        ? AppContext.BaseDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                       "Snail", "AndroidShortcutMaker");

    /// <summary>Resources directory where adb.exe and scrcpy.exe live.</summary>
    public static string ResourcesDir => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Resources")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                       "Snail", "Resources");

    public static string ConfigPath   => Path.Combine(DataDir, "config.json");
    public static string LogsDir      => Path.Combine(DataDir, "logs");
    public static string IconsDir     => Path.Combine(DataDir, "shortcut icons");
    public static string AdbPath      => Path.Combine(ResourcesDir, "adb.exe");
    public static string ScrcpyPath   => Path.Combine(ResourcesDir, "scrcpy.exe");
}
