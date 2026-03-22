using System.Windows;

namespace android_shortcut_maker;

public partial class App : Application
{
    public const string CurrentVersion = "v2.0.0.0";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _ = Updater.CheckForUpdateAsync(CurrentVersion);

        if (e.Args.Length > 0)
        {
            await ShortcutLauncher.TryLaunchFromShortcutAsync(e.Args);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
