using System.Windows;

namespace windows_phone_app_shortcut
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static readonly string CurrentVersion = "v1.0.7.0";

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0)
            {
                await ShortcutScrcpyLauncher.TryLaunchFromArgsAsync(e.Args);
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}
