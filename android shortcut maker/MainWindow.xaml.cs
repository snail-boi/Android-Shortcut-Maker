using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace android_shortcut_maker;

public partial class MainWindow : Window
{
    private ShortcutMakerConfig _config;
    private readonly string _iconsDir;
    private readonly DispatcherTimer _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        _config = ShortcutMakerConfigStore.Load();
        ApplyTheme(_config.UseDarkMode);

        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snail", "AndroidShortcutMaker");
        _iconsDir = Path.Combine(baseDir, "shortcut icons");
        Directory.CreateDirectory(_iconsDir);

        Loaded += MainWindow_Loaded;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await UpdateFooterStatusAsync();
        _statusTimer.Start();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
        await UpdateFooterStatusAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
        await UpdateFooterStatusAsync();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_config) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _config = ShortcutMakerConfigStore.Load();
            await UpdateFooterStatusAsync();
        }
    }

    private async Task RefreshAppsAsync()
    {
        var list = GetAppsList();
        list.Items.Clear();

        foreach (var extra in GetExtraApps())
        {
            list.Items.Add(extra);
        }

        var apps = await GetInstalledAppsAsync();
        foreach (var app in apps.OrderBy(x => x.PackageName, StringComparer.OrdinalIgnoreCase))
        {
            list.Items.Add(app);
        }
    }

    private ListBox GetAppsList() => AppsList;

    private List<AppInfo> GetExtraApps()
    {
        var resourcesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snail", "AndroidShortcutMaker", "Resources");

        string? ToIconUri(string fileName)
        {
            var fullPath = Path.Combine(resourcesDir, fileName);
            return File.Exists(fullPath) ? new Uri(fullPath).AbsoluteUri : null;
        }

        return
        [
            new AppInfo { PackageName = "audio-link", DisplayName = "audio link", Kind = ShortcutKind.AudioLink, IconPath = ToIconUri("AudioLink.png") },
            new AppInfo { PackageName = "screencast", DisplayName = "screencast", Kind = ShortcutKind.Screencast, IconPath = ToIconUri("Screencast.png") },
            new AppInfo { PackageName = "camera-front", DisplayName = "front camera", Kind = ShortcutKind.FrontCamera, IconPath = ToIconUri("Frontcamera.png") },
            new AppInfo { PackageName = "camera-back", DisplayName = "back camera", Kind = ShortcutKind.BackCamera, IconPath = ToIconUri("Backcamera.png") }
        ];
    }

    private async Task<List<AppInfo>> GetInstalledAppsAsync()
    {
        var result = new List<AppInfo>();
        var adb = _config.Paths.Adb;

        if (!File.Exists(adb))
        {
            MessageBox.Show($"adb not found at {adb}", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return result;
        }

        var currentDevice = await ResolveCurrentDeviceForListAsync();
        if (string.IsNullOrWhiteSpace(currentDevice))
        {
            return result;
        }

        var output = await AdbRunner.RunCaptureAsync(adb, $"-s {currentDevice} shell pm list packages -f -3");
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(line, "package:(.+)=(.+)");
            if (!m.Success)
            {
                continue;
            }

            var package = m.Groups[2].Value.Trim();
            result.Add(new AppInfo
            {
                PackageName = package,
                DisplayName = package,
                Kind = ShortcutKind.InstalledApp,
                IconPath = FindExistingIcon(package)
            });
        }

        return result;
    }

    private async Task<string?> ResolveCurrentDeviceForListAsync()
    {
        return await ShortcutLauncher.ResolveDeviceAsync(_config.Paths.Adb, _config.SelectedDeviceUSB, _config.SelectedDeviceWiFi, allowPortRecoveryPrompt: false);
    }

    private async Task UpdateFooterStatusAsync()
    {
        var adb = _config.Paths.Adb;
        if (!File.Exists(adb))
        {
            FooterStatusText.Text = "Device: not configured | USB: - | Wi-Fi: - | Status: no device found";
            return;
        }

        var devices = await AdbRunner.RunCaptureAsync(adb, "devices");
        var lines = devices.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool usbConnected = !string.IsNullOrWhiteSpace(_config.SelectedDeviceUSB)
            && lines.Any(x => x.StartsWith(_config.SelectedDeviceUSB) && x.EndsWith("device"));

        bool wifiConnected = !string.IsNullOrWhiteSpace(_config.SelectedDeviceWiFi)
            && lines.Any(x => x.StartsWith(_config.SelectedDeviceWiFi) && x.EndsWith("device"));

        var status = usbConnected ? "USB connected" : wifiConnected ? "Wi-Fi connected" : "no device found";
        FooterStatusText.Text = $"Device: {_config.SelectedDeviceName} | USB: {_config.SelectedDeviceUSB} | Wi-Fi: {_config.SelectedDeviceWiFi} | Status: {status}";
    }

    private async void AppsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GetAppsList().SelectedItem is not AppInfo app)
        {
            return;
        }

        var optionArgs = new List<string>();
        if (app.Kind != ShortcutKind.FrontCamera && app.Kind != ShortcutKind.BackCamera)
        {
            var optionsWindow = new ShortcutOptionsWindow(app) { Owner = this };
            if (optionsWindow.ShowDialog() != true)
            {
                return;
            }

            optionArgs = optionsWindow.OptionArgs;
        }

        var nameDialog = new InputDialog("Shortcut Name", "Enter a name for the shortcut:", app.DisplayName)
        {
            Owner = this
        };

        if (nameDialog.ShowDialog() != true)
        {
            return;
        }

        var shortcutName = nameDialog.InputText;
        if (string.IsNullOrWhiteSpace(shortcutName))
        {
            MessageBox.Show("Shortcut name cannot be empty.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var iconPath = await ResolveIconPathAsync(app, promptForUploadWhenMissing: true);
        var args = BuildShortcutArgs(app, shortcutName, optionArgs);
        CreateShortcut(shortcutName, args, iconPath);

        MessageBox.Show("Shortcut created on Desktop.", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Information);
        await UpdateFooterStatusAsync();
    }

    private async void GenerateIconsButton_Click(object sender, RoutedEventArgs e)
    {
        GenerateIconsButton.IsEnabled = false;
        var oldText = GenerateIconsButton.Content;
        GenerateIconsButton.Content = "Collecting app icons...";

        int created = 0, skipped = 0, failed = 0;
        foreach (var item in GetAppsList().Items)
        {
            if (item is not AppInfo app || app.Kind != ShortcutKind.InstalledApp)
            {
                continue;
            }

            var existing = FindExistingIcon(app.PackageName);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                skipped++;
                continue;
            }

            var extracted = await ExtractAppIconAsync(app.PackageName);
            if (string.IsNullOrWhiteSpace(extracted) || !File.Exists(extracted))
            {
                failed++;
                continue;
            }

            var dest = Path.Combine(_iconsDir, app.PackageName + ".png");
            try
            {
                File.Copy(extracted, dest, true);
                TryCreateIco(dest);
                created++;
            }
            catch
            {
                failed++;
            }
        }

        GenerateIconsButton.Content = oldText;
        GenerateIconsButton.IsEnabled = true;

        await RefreshAppsAsync();
        MessageBox.Show($"Icons created: {created}\nSkipped: {skipped}\nFailed: {failed}", "android shortcut maker", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string BuildShortcutArgs(AppInfo app, string shortcutName, List<string> optionArgs)
    {
        var args = new List<string>
        {
            $"--window-title=\"{shortcutName}\""
        };

        if (app.Kind == ShortcutKind.InstalledApp)
        {
            args.Add("--new-display");
            args.Add("--no-vd-system-decorations");
            args.Add($"--start-app={app.PackageName}");
        }
        else if (app.Kind == ShortcutKind.FrontCamera)
        {
            args.Add("--video-source=camera");
            args.Add("--camera-facing=front");
        }
        else if (app.Kind == ShortcutKind.BackCamera)
        {
            args.Add("--video-source=camera");
            args.Add("--camera-facing=back");
        }

        foreach (var option in optionArgs)
        {
            if (!args.Contains(option, StringComparer.OrdinalIgnoreCase))
            {
                args.Add(option);
            }
        }

        if (!string.IsNullOrWhiteSpace(_config.SelectedDeviceName))
        {
            args.Add($"--target-name=\"{_config.SelectedDeviceName}\"");
        }

        if (!string.IsNullOrWhiteSpace(_config.SelectedDeviceUSB))
        {
            args.Add($"--target-usb={_config.SelectedDeviceUSB}");
        }

        if (!string.IsNullOrWhiteSpace(_config.SelectedDeviceWiFi))
        {
            args.Add($"--target-wifi={_config.SelectedDeviceWiFi}");
        }

        return string.Join(" ", args);
    }

    private async Task<string?> ResolveIconPathAsync(AppInfo app, bool promptForUploadWhenMissing)
    {
        if (!string.IsNullOrWhiteSpace(app.IconPath))
        {
            try
            {
                var presetPath = app.IconPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(app.IconPath).LocalPath
                    : app.IconPath;

                if (File.Exists(presetPath))
                {
                    return presetPath;
                }
            }
            catch
            {
            }
        }

        var existing = FindExistingIcon(app.PackageName);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        if (app.Kind == ShortcutKind.InstalledApp)
        {
            var extracted = await ExtractAppIconAsync(app.PackageName);
            if (!string.IsNullOrWhiteSpace(extracted) && File.Exists(extracted))
            {
                var dest = Path.Combine(_iconsDir, app.PackageName + ".png");
                try
                {
                    File.Copy(extracted, dest, true);
                    TryCreateIco(dest);
                    return dest;
                }
                catch
                {
                    return extracted;
                }
            }
        }

        if (promptForUploadWhenMissing)
        {
            var res = MessageBox.Show("No icon found. Do you want to upload one?", "android shortcut maker", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files|*.*"
                };

                if (dlg.ShowDialog(this) == true)
                {
                    var ext = Path.GetExtension(dlg.FileName);
                    var dest = Path.Combine(_iconsDir, app.PackageName + ext);
                    File.Copy(dlg.FileName, dest, true);
                    TryCreateIco(dest);
                    return dest;
                }
            }
        }

        return null;
    }

    private string? FindExistingIcon(string packageName)
    {
        try
        {
            var png = Path.Combine(_iconsDir, packageName + ".png");
            if (File.Exists(png)) return new Uri(png).AbsoluteUri;

            var ico = Path.Combine(_iconsDir, packageName + ".ico");
            if (File.Exists(ico)) return new Uri(ico).AbsoluteUri;
        }
        catch
        {
        }

        return null;
    }

    private void CreateShortcut(string name, string args, string? iconPath)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "android shortcut maker.exe");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, name + ".lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available");

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);

        shortcut.TargetPath = exePath;
        shortcut.Arguments = args;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var localIcon = iconPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(iconPath).LocalPath
                : iconPath;

            if (File.Exists(localIcon))
            {
                var ext = Path.GetExtension(localIcon);
                if (!string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
                {
                    var ico = TryCreateIco(localIcon);
                    if (!string.IsNullOrWhiteSpace(ico) && File.Exists(ico))
                    {
                        localIcon = ico;
                    }
                }

                if (File.Exists(localIcon))
                {
                    shortcut.IconLocation = localIcon + ",0";
                }
            }
        }

        shortcut.Save();
    }

    private string? TryCreateIco(string imagePath)
    {
        try
        {
            var icoPath = Path.Combine(_iconsDir, Path.GetFileNameWithoutExtension(imagePath) + ".ico");
            if (string.Equals(Path.GetExtension(imagePath), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(imagePath, icoPath, true);
                return icoPath;
            }

            var pngBytes = File.ReadAllBytes(imagePath);

            int width;
            int height;
            using (var fs = File.OpenRead(imagePath))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            using var outFs = File.Create(icoPath);
            using var bw = new BinaryWriter(outFs);
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)1);

            bw.Write((byte)(width >= 256 ? 0 : width));
            bw.Write((byte)(height >= 256 ? 0 : height));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)0);
            bw.Write((ushort)32);
            bw.Write((uint)pngBytes.Length);
            bw.Write((uint)(6 + 16));
            bw.Write(pngBytes);

            return icoPath;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ExtractAppIconAsync(string packageName)
    {
        try
        {
            var device = await ResolveCurrentDeviceForListAsync();
            if (string.IsNullOrWhiteSpace(device)) return null;

            var adb = _config.Paths.Adb;
            var outp = await AdbRunner.RunCaptureAsync(adb, $"-s {device} shell pm path {packageName}");
            var match = Regex.Match(outp, "package:(.+)");
            if (!match.Success)
            {
                return null;
            }

            var apkOnDevice = match.Groups[1].Value.Trim();
            var tempDir = Path.Combine(Path.GetTempPath(), "android_shortcut_maker_pull");
            Directory.CreateDirectory(tempDir);

            var localApk = Path.Combine(tempDir, packageName + ".apk");
            await AdbRunner.RunCaptureAsync(adb, $"-s {device} pull \"{apkOnDevice}\" \"{localApk}\"");
            if (!File.Exists(localApk))
            {
                return null;
            }

            return FindIconInApk(localApk, tempDir);
        }
        catch
        {
            return null;
        }
    }

    private string? FindIconInApk(string apkPath, string outputDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(apkPath);
            var candidates = archive.Entries
                .Where(e =>
                    (e.FullName.StartsWith("res/drawable", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.StartsWith("res/mipmap", StringComparison.OrdinalIgnoreCase))
                    && e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!candidates.Any())
            {
                return null;
            }

            var selected = candidates.OrderByDescending(e => e.FullName).First();
            var outPath = Path.Combine(outputDir, Path.GetFileName(selected.FullName));

            using var src = selected.Open();
            using var fs = File.Create(outPath);
            src.CopyTo(fs);
            return outPath;
        }
        catch
        {
            return null;
        }
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _config.UseDarkMode = !_config.UseDarkMode;
        ApplyTheme(_config.UseDarkMode);
        ShortcutMakerConfigStore.Save(_config);
    }

    private void ApplyTheme(bool dark)
    {
        Resources["WindowBackgroundBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 244, 248));

        Resources["CardBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));

        Resources["PrimaryTextBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));

        Resources["SubtleTextBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));

        Resources["IconTileBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 246, 255));

        Resources["ItemHoverBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 249, 255));

        Resources["ItemSelectedBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 58, 95))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 243, 255));

        Resources["ChevronBrush"] = dark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219));

        ThemeToggleButton.Content = dark ? "Light Mode" : "Dark Mode";
    }
}
