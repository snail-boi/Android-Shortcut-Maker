using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace android_shortcut_maker;

public static class Updater
{
    private const string RepoOwner = "snail-boi";
    private const string RepoName = "Android-Shortcut-Maker";
    private const string PreferredInstallerPrefix = "Android.Shortcut.Maker";

    public static async Task CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidShortcutMakerUpdater/1.0");

            var apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
            var json = await client.GetStringAsync(apiUrl).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return;

            JsonElement? latestRelease = null;
            Version? latestNumericVersion = null;

            foreach (var release in root.EnumerateArray())
            {
                if (!release.TryGetProperty("tag_name", out var tagElem))
                    continue;

                var tagName = tagElem.GetString();
                if (string.IsNullOrWhiteSpace(tagName))
                    continue;

                var match = Regex.Match(tagName, @"\d+(\.\d+)*");
                if (!match.Success)
                    continue;

                if (!Version.TryParse(match.Value, out var releaseVersion))
                    continue;

                if (latestNumericVersion == null || releaseVersion > latestNumericVersion)
                {
                    latestNumericVersion = releaseVersion;
                    latestRelease = release;
                }
            }

            if (latestRelease == null)
                return;

            var latestTag = latestRelease.Value.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!IsNewerVersion(latestTag, currentVersion))
                return;

            if (!latestRelease.Value.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
                return;

            JsonElement? installerAsset = null;
            foreach (var asset in assetsElem.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElem))
                    continue;

                var name = nameElem.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (name.StartsWith(PreferredInstallerPrefix, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    installerAsset = asset;
                    break;
                }

                if (installerAsset == null && name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    installerAsset = asset;
                }
            }

            if (installerAsset == null)
                return;

            var installerName = installerAsset.Value.GetProperty("name").GetString() ?? "android-shortcut-maker.msi";
            var downloadUrl = installerAsset.Value.GetProperty("browser_download_url").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(downloadUrl))
                return;

            var releaseNotes = latestRelease.Value.TryGetProperty("body", out var body)
                ? (body.GetString() ?? "No patch notes available.").Trim()
                : "No patch notes available.";

            var result = MessageBox.Show(
                $"A new version {latestTag} is available!\n\nPatch notes:\n\n{releaseNotes}\n\nDo you want to download and install it?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;

            var tempPath = Path.Combine(Path.GetTempPath(), installerName);
            var data = await client.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            await File.WriteAllBytesAsync(tempPath, data).ConfigureAwait(false);

            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            await Application.Current.Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
        }
        catch
        {
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        var cleanLatest = latest.TrimStart('v', 'V').Trim();
        var cleanCurrent = current.TrimStart('v', 'V').Trim();

        var rx = new Regex(@"\d+");
        var latestNumbers = rx.Matches(cleanLatest);
        var currentNumbers = rx.Matches(cleanCurrent);

        var len = Math.Min(latestNumbers.Count, currentNumbers.Count);
        for (var i = 0; i < len; i++)
        {
            var lv = int.Parse(latestNumbers[i].Value);
            var cv = int.Parse(currentNumbers[i].Value);
            if (lv > cv) return true;
            if (lv < cv) return false;
        }

        return string.Compare(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
