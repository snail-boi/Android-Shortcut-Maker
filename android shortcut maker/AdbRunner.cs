using System.Diagnostics;
using System.IO;
using System.Text;

namespace android_shortcut_maker;

internal static class AdbRunner
{
    public static async Task<string> RunCaptureAsync(string adbPath, string args)
    {
        if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
        {
            return string.Empty;
        }

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return string.Empty;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;
        if (!string.IsNullOrWhiteSpace(error))
        {
            output = string.Concat(output, Environment.NewLine, error).Trim();
        }

        return output;
    }

    public static async Task<int> RunAsync(string adbPath, string args)
    {
        if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
        {
            return -1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return -1;
        }

        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
