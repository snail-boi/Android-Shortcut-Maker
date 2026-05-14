using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace android_shortcut_maker;

/// <summary>
/// Manages the single shared audio-only scrcpy process used by all
/// Shortcut Control instances. Thread-safe. Process-safe via a named
/// mutex so multiple ASM processes coordinate correctly.
/// </summary>
internal static class SharedAudioSession
{
    private const string MutexName = "ASM_AudioSession_Mutex";

    private static Process? _audioProcess;
    private static string _audioDevice = string.Empty;
    private static int _scInstanceCount;
    private static readonly object Lock = new();

    // ── SC instance tracking ─────────────────────────────────────────────────

    /// <summary>Called by each ShortcutHost on startup.</summary>
    public static void RegisterScInstance()
    {
        lock (Lock)
        {
            _scInstanceCount++;
            Debugger.Show($"[AudioSession] SC instance registered. Total={_scInstanceCount}");
        }
    }

    /// <summary>Called by each ShortcutHost on shutdown.</summary>
    public static void UnregisterScInstance()
    {
        lock (Lock)
        {
            _scInstanceCount = Math.Max(0, _scInstanceCount - 1);
            Debugger.Show($"[AudioSession] SC instance unregistered. Total={_scInstanceCount}");

            if (_scInstanceCount == 0)
                StopInternal();
        }
    }

    // ── Audio toggle ─────────────────────────────────────────────────────────

    public static int AudioProcessId
    {
        get
        {
            lock (Lock)
                return (_audioProcess != null && !_audioProcess.HasExited) ? _audioProcess.Id : 0;
        }
    }

    public static bool IsRunning
    {
        get
        {
            lock (Lock)
                return _audioProcess != null && !_audioProcess.HasExited;
        }
    }

    /// <summary>
    /// Toggle the shared audio session. If not running, starts it on
    /// the given device using the given scrcpy path. If running, stops it.
    /// </summary>
    public static void Toggle(string deviceSerial, string scrcpyPath, string[] originalArgs)
    {
        lock (Lock)
        {
            if (IsRunning)
            {
                Debugger.Show("[AudioSession] Toggle: stopping audio session.");
                StopInternal();
            }
            else
            {
                Debugger.Show($"[AudioSession] Toggle: starting audio session on '{deviceSerial}'.");
                StartInternal(deviceSerial, scrcpyPath, originalArgs);
            }
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static void StartInternal(string deviceSerial, string scrcpyPath, string[] originalArgs)
    {
        // Build audio-only args from the original shortcut args, stripping
        // any video/window related args and forcing audio-only mode.
        var audioArgs = BuildAudioArgs(originalArgs);

        var psi = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(deviceSerial);
        foreach (var arg in audioArgs)
            psi.ArgumentList.Add(arg);

        _audioProcess = Process.Start(psi);
        _audioDevice = deviceSerial;

        Debugger.Show($"[AudioSession] Started audio scrcpy pid={_audioProcess?.Id} device='{deviceSerial}' args={string.Join(" ", audioArgs)}");
    }

    private static void StopInternal()
    {
        if (_audioProcess == null) return;
        try
        {
            if (!_audioProcess.HasExited)
            {
                _audioProcess.Kill();
                Debugger.Show($"[AudioSession] Killed audio scrcpy pid={_audioProcess.Id}");
            }
        }
        catch (Exception ex)
        {
            Debugger.Show("[AudioSession] Kill failed: " + ex.Message);
        }
        finally
        {
            _audioProcess = null;
            _audioDevice = string.Empty;
        }
    }

    private static List<string> BuildAudioArgs(string[] originalArgs)
    {
        // Strip all video, window, and display args from the original set.
        // Keep audio codec/buffer args so quality settings are respected.
        var videoRelated = new[]
        {
            "--no-video", "--video-source", "--video-codec", "--video-bit-rate",
            "--video-buffer", "--max-size", "--new-display", "--no-vd-system-decorations",
            "--start-app", "--always-on-top", "--window-title", "--turn-screen-off",
            "--power-off-on-close", "--stay-awake", "--no-window"
        };

        var clean = ShortcutLauncher.SanitizeArgsPublic(originalArgs)
            .Where(a => !videoRelated.Any(v =>
                a.Equals(v, StringComparison.OrdinalIgnoreCase)
                || a.StartsWith(v + "=", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Force audio-only mode.
        clean.RemoveAll(a => a.Equals("--no-audio", StringComparison.OrdinalIgnoreCase));
        clean.Add("--no-video");
        clean.Add("--no-window");

        // Ensure audio source is set to playback if not already specified.
        if (!clean.Any(a => a.StartsWith("--audio-source", StringComparison.OrdinalIgnoreCase)))
            clean.Add("--audio-source=playback");

        return clean;
    }
}