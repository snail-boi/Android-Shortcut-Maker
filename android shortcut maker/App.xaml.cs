using System.Windows;
using System.Windows.Media;

namespace android_shortcut_maker;

public partial class App : Application
{
    public const string CurrentVersion = "v2.0.0.0";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Debugger.IsEnabled = true;

        var config = ShortcutMakerConfigStore.Load();
        ApplyTheme(config.UseDarkMode);

        _ = Updater.CheckForUpdateAsync(CurrentVersion);

        if (e.Args.Length > 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await ShortcutLauncher.TryLaunchFromShortcutAsync(e.Args);
            if (!ShortcutLauncher.ShouldStayResident)
                Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    internal static void ApplyTheme(bool dark)
    {
        var res = Application.Current.Resources;

        Set(res, "AccentBrush", 62, 123, 255);
        Set(res, "AccentHoverBrush", 90, 139, 255);
        Set(res, "WindowBackgroundBrush", dark ? 30 : 240, dark ? 30 : 244, dark ? 30 : 248);
        Set(res, "CardBrush", dark ? 43 : 255, dark ? 43 : 255, dark ? 43 : 255);
        Set(res, "PrimaryTextBrush", dark ? 243 : 17, dark ? 243 : 24, dark ? 243 : 39);
        Set(res, "SubtleTextBrush", dark ? 160 : 107, dark ? 160 : 114, dark ? 160 : 128);
        Set(res, "IconTileBrush", dark ? 60 : 239, dark ? 60 : 246, dark ? 60 : 255);
        Set(res, "ItemHoverBrush", dark ? 60 : 241, dark ? 60 : 249, dark ? 60 : 255);
        Set(res, "ItemSelectedBrush", dark ? 50 : 232, dark ? 80 : 243, dark ? 130 : 255);
        Set(res, "ChevronBrush", dark ? 140 : 209, dark ? 140 : 213, dark ? 140 : 219);
        Set(res, "BorderBrush", dark ? 60 : 229, dark ? 60 : 231, dark ? 60 : 235);
        Set(res, "InputBackgroundBrush", dark ? 55 : 255, dark ? 55 : 255, dark ? 55 : 255);
        Set(res, "DisabledTextBrush", dark ? 100 : 176, dark ? 100 : 183, dark ? 100 : 195);
    }

    private static void Set(ResourceDictionary res, string key, int r, int g, int b)
    {
        var color = Color.FromRgb((byte)r, (byte)g, (byte)b);
        if (res.Contains(key) && res[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            res[key] = new SolidColorBrush(color);
    }
}