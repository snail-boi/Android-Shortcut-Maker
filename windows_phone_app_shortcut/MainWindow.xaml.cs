using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace windows_phone_app_shortcut
{
    public class AppInfo
    {
        public string PackageName { get; set; }
        public string DisplayName { get; set; }
        // new: optional URI string pointing to an icon file (prefixed with file:///)
        public string? IconPath { get; set; }
        public string? CustomArgs { get; set; }
        public bool RequiresAudioPrompt { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly string resourcesPath;
        public MainWindow()
        {
            InitializeComponent();
            Updater.CheckForUpdateAsync(App.CurrentVersion);
            resourcesPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snail", "Resources");
            Loaded += MainWindow_Loaded;
        }

        private ListBox GetAppsList() => (ListBox)FindName("AppsList");

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshApps();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshApps();
        }

        private async Task RefreshApps()
        {
            var listBox = GetAppsList();
            listBox.Items.Clear();
            var apps = await GetInstalledApps();
            var extraApps = GetExtraApps();
            foreach (var extra in extraApps)
            {
                listBox.Items.Add(extra);
            }
            foreach (var a in apps.OrderBy(x => x.DisplayName))
            {
                listBox.Items.Add(a);
            }
        }

        private Task<List<AppInfo>> GetInstalledApps()
        {
            return Task.Run(() =>
            {
                var list = new List<AppInfo>();
                var adb = Path.Combine(resourcesPath, "adb.exe");
                if (!File.Exists(adb))
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"adb not found at {adb}"));
                    return list;
                }

                var start = new ProcessStartInfo(adb, "shell pm list packages -f -3")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                try
                {
                    var p = Process.Start(start);
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var m = Regex.Match(line, "package:(.+)=(.+)");
                        if (m.Success)
                        {
                            var apkPath = m.Groups[1].Value.Trim();
                            var package = m.Groups[2].Value.Trim();

                            // Try to resolve an existing icon from the resources "shortcut icons" folder
                            string? iconUri = null;
                            try
                            {
                                var iconsDir = Path.Combine(resourcesPath, "shortcut icons");
                                var existingIco = Path.Combine(iconsDir, package + ".ico");
                                var existingPng = Path.Combine(iconsDir, package + ".png");
                                // Prefer PNG for UI image binding because WPF Image does not always display .ico files
                                if (File.Exists(existingPng))
                                {
                                    iconUri = new Uri(existingPng).AbsoluteUri;
                                }
                                else if (File.Exists(existingIco))
                                {
                                    iconUri = new Uri(existingIco).AbsoluteUri;
                                }
                            }
                            catch
                            {
                                // ignore any path issues; leave iconUri null
                            }

                            list.Add(new AppInfo { PackageName = package, DisplayName = package, IconPath = iconUri });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show("Failed to run adb: " + ex.Message));
                }

                return list;
            });
        }

        private List<AppInfo> GetExtraApps()
        {
            return new List<AppInfo>
            {
                new AppInfo
                {
                    PackageName = "audio-link",
                    DisplayName = "Audio Link",
                    CustomArgs = "--no-video --no-window --audio-source=playback",
                    RequiresAudioPrompt = false
                },
                new AppInfo
                {
                    PackageName = "screenshare",
                    DisplayName = "Screenshare",
                    CustomArgs = string.Empty,
                    RequiresAudioPrompt = true
                },
                new AppInfo
                {
                    PackageName = "camera-front",
                    DisplayName = "Camera Front",
                    CustomArgs = "--camera-facing=front",
                    RequiresAudioPrompt = false
                },
                new AppInfo
                {
                    PackageName = "camera-back",
                    DisplayName = "Camera Back",
                    CustomArgs = "--camera-facing=back",
                    RequiresAudioPrompt = false
                }
            };
        }

        private async void AppsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var listBox = GetAppsList();
            if (listBox.SelectedItem is AppInfo app)
            {
                bool includeAudio = false;
                bool promptAudio = app.RequiresAudioPrompt || string.IsNullOrWhiteSpace(app.CustomArgs);
                if (promptAudio)
                {
                    var res = MessageBox.Show($"Create shortcut for {app.DisplayName}?\nYes = audio\nNo = no audio\nCancel = cancel", "Create Shortcut", MessageBoxButton.YesNoCancel);
                    if (res == MessageBoxResult.Cancel) return;
                    includeAudio = res == MessageBoxResult.Yes;
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
                    MessageBox.Show("Shortcut name cannot be empty.", "Create Shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var iconsDir = Path.Combine(resourcesPath, "shortcut icons");
                    Directory.CreateDirectory(iconsDir);
                    var existingIco = Path.Combine(iconsDir, app.PackageName + ".ico");
                    var existingPng = Path.Combine(iconsDir, app.PackageName + ".png");

                    string? iconPath = null;
                    if (File.Exists(existingIco))
                    {
                        iconPath = existingIco;
                    }
                    else if (File.Exists(existingPng))
                    {
                        iconPath = existingPng;
                    }
                    else if (string.IsNullOrWhiteSpace(app.CustomArgs))
                    {
                        var extracted = await ExtractAppIcon(app.PackageName);
                        if (!string.IsNullOrEmpty(extracted) && File.Exists(extracted))
                        {
                            var destPng = Path.Combine(iconsDir, app.PackageName + Path.GetExtension(extracted));
                            try
                            {
                                File.Copy(extracted, destPng, true);
                                iconPath = destPng;
                            }
                            catch
                            {
                                iconPath = extracted;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                    {
                        MessageBox.Show("Icon not found for this app. Shortcut will still be created without a custom icon.", "Icon Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        iconPath = null;
                    }

                    var shortcutArgs = BuildShortcutArgs(app, shortcutName, includeAudio);
                    CreateShortcutForApp(shortcutName, shortcutArgs, iconPath);
                    MessageBox.Show("Shortcut created on Desktop.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to create shortcut: " + ex.Message);
                }
            }
        }

        private string BuildShortcutArgs(AppInfo app, string shortcutName, bool includeAudio)
        {
            var args = new List<string>
            {
                $"--window-title=\"{shortcutName}\""
            };

            if (string.IsNullOrWhiteSpace(app.CustomArgs))
            {
                args.Add("--new-display");
                args.Add("--no-vd-system-decorations");
                args.Add($"--start-app={app.PackageName}");
            }
            else if (!string.IsNullOrWhiteSpace(app.CustomArgs))
            {
                args.AddRange(app.CustomArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            if (app.RequiresAudioPrompt || string.IsNullOrWhiteSpace(app.CustomArgs))
            {
                args.Add(includeAudio ? "--audio-source=playback" : "--no-audio");
            }

            return string.Join(" ", args);
        }

        private void CreateShortcutForApp(string name, string args, string? iconPath)
        {
            var targetExe = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scarlet Phone Shortcuts.exe");

            var config = ShortcutConfigLoader.Load();
            var deviceName = config.SelectedDeviceName;
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                args += $" --device-name=\"{deviceName}\"";
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var lnkPath = Path.Combine(desktop, name + ".lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new Exception("WScript.Shell COM object not available");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(lnkPath);

            shortcut.TargetPath = targetExe;
            shortcut.Arguments = args;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
            if (!string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    if (iconPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        try { iconPath = new Uri(iconPath).LocalPath; } catch { }
                    }

                    if (File.Exists(iconPath))
                    {
                        var iconsDir = Path.Combine(resourcesPath, "shortcut icons");
                        Directory.CreateDirectory(iconsDir);
                        var icoPath = Path.Combine(iconsDir, Path.GetFileNameWithoutExtension(iconPath) + ".ico");

                        var ext = Path.GetExtension(iconPath);
                        if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                File.Copy(iconPath, icoPath, true);
                            }
                            catch
                            {
                            }

                            var finalIco = File.Exists(icoPath) ? icoPath : iconPath;
                            if (File.Exists(finalIco))
                            {
                                shortcut.IconLocation = finalIco + ",0";
                            }
                        }
                        else
                        {
                            try
                            {
                                ConvertPngToIco(iconPath, icoPath);
                                if (File.Exists(icoPath))
                                {
                                    shortcut.IconLocation = icoPath + ",0";
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            shortcut.Save();
        }

        private async Task<string?> ExtractAppIcon(string packageName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var adb = Path.Combine(resourcesPath, "adb.exe");
                    if (!File.Exists(adb))
                    {
                        // do not throw; return null so callers can handle gracefully
                        Dispatcher.Invoke(() => MessageBox.Show($"adb not found at {adb}"));
                        return (string?)null;
                    }

                    var start = new ProcessStartInfo(adb, $"shell pm path {packageName}")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var p = Process.Start(start);
                    var outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    var m = Regex.Match(outp, "package:(.+)");
                    if (!m.Success) return null;
                    var apkPathOnDevice = m.Groups[1].Value.Trim();

                    var tmp = Path.Combine(Path.GetTempPath(), "app_pull");
                    Directory.CreateDirectory(tmp);
                    var localApk = Path.Combine(tmp, packageName + ".apk");

                    var pullStart = new ProcessStartInfo(adb, $"pull \"{apkPathOnDevice}\" \"{localApk}\"")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var p2 = Process.Start(pullStart);
                    var pullOut = p2.StandardOutput.ReadToEnd();
                    p2.WaitForExit(10000);
                    if (!File.Exists(localApk)) return null;

                    var iconFile = FindIconInApk(localApk, tmp);
                    if (iconFile == null) return null;

                    return iconFile;
                }
                catch (Exception ex)
                {
                    // log for debugging but do not throw to avoid crashes
                    Debug.WriteLine("ExtractAppIcon failed: " + ex);
                    return null;
                }
            });
        }

        private string? FindIconInApk(string apkPath, string outputDir)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(apkPath))
                {
                    var candidates = archive.Entries.Where(e => (e.FullName.StartsWith("res/drawable", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("res/mipmap", StringComparison.OrdinalIgnoreCase)) && e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (!candidates.Any()) return null;

                    var selected = candidates.OrderByDescending(e => e.FullName).First();
                    var outPath = Path.Combine(outputDir, Path.GetFileName(selected.FullName));
                    using (var s = selected.Open())
                    using (var fs = File.Create(outPath))
                    {
                        s.CopyTo(fs);
                    }
                    return outPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FindIconInApk failed: " + ex);
                return null;
            }
        }

        private void ConvertPngToIco(string pngPath, string icoPath)
        {
            // Read png bytes
            var pngBytes = File.ReadAllBytes(pngPath);

            // Get dimensions using BitmapDecoder
            int width;
            int height;
            using (var fs = File.OpenRead(pngPath))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            using (var outFs = File.Create(icoPath))
            using (var bw = new BinaryWriter(outFs))
            {
                // ICONDIR
                bw.Write((ushort)0); // reserved
                bw.Write((ushort)1); // type = 1 for icons
                bw.Write((ushort)1); // count

                // ICONDIRENTRY (16 bytes)
                bw.Write((byte)(width >= 256 ? 0 : width)); // bWidth
                bw.Write((byte)(height >= 256 ? 0 : height)); // bHeight
                bw.Write((byte)0); // bColorCount
                bw.Write((byte)0); // bReserved
                bw.Write((ushort)0); // wPlanes
                bw.Write((ushort)32); // wBitCount
                bw.Write((uint)pngBytes.Length); // dwBytesInRes
                bw.Write((uint)(6 + 16)); // dwImageOffset (header + entry)

                // image data (PNG)
                bw.Write(pngBytes);
            }
        }

        private string EnsureNoConsoleVbs(string scrcpyPath)
        {
            Directory.CreateDirectory(resourcesPath);
            var vbsPath = Path.Combine(resourcesPath, "scrcpy-noconsole.vbs");
            // VBScript: check for connected adb device, show message box if none, then run scrcpy via cmd /c so console does not appear; keep escape sequences intact
            var vbs = "On Error Resume Next\r\n" +
                      "devicesOut = \"\"\r\n" +
                      "Set execObj = CreateObject(\"WScript.Shell\").Exec(\"cmd /c adb.exe devices\")\r\n" +
                      "If Not execObj Is Nothing Then devicesOut = execObj.StdOut.ReadAll\r\n" +
                      "On Error GoTo 0\r\n" +
                      "hasDevice = False\r\n" +
                      "lines = Split(devicesOut, vbNewLine)\r\n" +
                      "For Each l In lines\r\n" +
                      "    l = Trim(l)\r\n" +
                      "    If l <> \"\" And InStr(l, \"List of devices attached\") = 0 Then\r\n" +
                      "        If InStr(l, \"device\") > 0 Then\r\n" +
                      "            hasDevice = True\r\n" +
                      "            Exit For\r\n" +
                      "        End If\r\n" +
                      "    End If\r\n" +
                      "Next\r\n" +
                      "If Not hasDevice Then\r\n" +
                      "    MsgBox \"No device connected via USB. Please connect a device and enable USB debugging.\", vbExclamation, \"No Device\"\r\n" +
                      "    WScript.Quit\r\n" +
                      "End If\r\n\r\n" +
                      "strCommand = \"cmd /c scrcpy.exe\"\r\n" +
                      "For Each Arg In WScript.Arguments\r\n" +
                      "    strCommand = strCommand & \" \"\"\" & Replace(Arg, \"\"\"\", \"\"\"\"\"\") & \"\"\"\"\r\n" +
                      "Next\r\n" +
                      "CreateObject(\"Wscript.Shell\").Run strCommand, 0, false\r\n";

            // If file missing or different, write/overwrite
            try
            {
                if (!File.Exists(vbsPath) || File.ReadAllText(vbsPath) != vbs)
                {
                    File.WriteAllText(vbsPath, vbs);
                }
            }
            catch
            {
                // ignore write failures; shortcut creation will fail later if script missing
            }

            return vbsPath;
        }

        // New: generate icons for all apps sequentially
        private async void GenerateIconsButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)FindName("GenerateIconsButton");
            if (btn == null) return;

            btn.IsEnabled = false;
            var oldContent = btn.Content;
            btn.Content = "Collecting app icons...";

            int created = 0, skipped = 0, failed = 0;
            var listBox = GetAppsList();
            // place icons in the same folder used by CreateShortcutForApp
            var iconsDir = Path.Combine(resourcesPath, "shortcut icons");
            Directory.CreateDirectory(iconsDir);

            // Iterate sequentially
            foreach (var item in listBox.Items)
            {
                if (!(item is AppInfo app)) continue;

                var icoPath = Path.Combine(iconsDir, app.PackageName + ".ico");
                var pngPath = Path.Combine(iconsDir, app.PackageName + ".png");

                // if either exists, skip pulling APK
                if (File.Exists(icoPath) || File.Exists(pngPath))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var extractedPng = await ExtractAppIcon(app.PackageName);
                    if (string.IsNullOrEmpty(extractedPng) || !File.Exists(extractedPng))
                    {
                        failed++;
                        continue;
                    }

                    // copy png into shortcut icons folder so we won't repull later
                    try
                    {
                        File.Copy(extractedPng, pngPath, true);
                    }
                    catch
                    {
                        // ignore copy failures
                    }

                    // create ico
                    try
                    {
                        ConvertPngToIco(pngPath, icoPath);
                    }
                    catch
                    {
                        // continue even if conversion fails
                    }

                    created++;
                }
                catch
                {
                    failed++;
                }
            }

            btn.Content = oldContent;
            btn.IsEnabled = true;

            // refresh list so newly created icons are visible in the UI
            try
            {
                await RefreshApps();
            }
            catch { }

            MessageBox.Show($"Icons created: {created}\nSkipped: {skipped}\nFailed: {failed}", "Generate App Icons");
        }
    }
}