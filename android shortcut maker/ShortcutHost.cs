using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace android_shortcut_maker;

internal static class ShortcutHost
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyAudio = 1;
    private const int HotkeyPlay = 2;
    private const int HotkeyNext = 3;
    private const int HotkeyPrev = 4;
    private const int HotkeyVolUp = 5;
    private const int HotkeyVolDown = 6;
    private const int HotkeyQuality = 7;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static ShortcutHostConfig? _cfg;
    private static HwndSource? _hwndSource;
    private static DispatcherTimer? _watchdogTimer;
    private static ConnectionMonitor? _connectionMonitor;

    public static void Start(ShortcutHostConfig cfg)
    {
        _cfg = cfg;

        var sink = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden
        };
        sink.Show();
        sink.Hide();

        var helper = new WindowInteropHelper(sink);
        helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        if (cfg.ShortcutControlEnabled)
            RegisterHotkeys(helper.Handle, cfg);

        SharedAudioSession.RegisterScInstance();

        // If the shortcut had audio source specified, start the shared audio session.
        var wantsAudio = cfg.OriginalArgs.Any(a =>
            a.StartsWith("--audio-source", StringComparison.OrdinalIgnoreCase));

        if (wantsAudio && !SharedAudioSession.IsRunning)
            SharedAudioSession.Toggle(cfg.DeviceSerial, cfg.ScrcpyPath, cfg.OriginalArgs);

        if (cfg.ConnectionAwareEnabled && !string.IsNullOrWhiteSpace(cfg.UsbSerial))
        {
            _connectionMonitor = new ConnectionMonitor(
                usbSerial: cfg.UsbSerial,
                mdnsServiceName: cfg.MdnsServiceName,
                initialSerial: cfg.DeviceSerial,
                onSwitchNeeded: OnConnectionSwitch);
            _connectionMonitor.Start();
        }

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchdogTimer.Tick += (_, _) => CheckScrcpy();
        _watchdogTimer.Start();

        Debugger.Show($"[Host:{cfg.ScId}] Started. sc={cfg.ShortcutControlEnabled} ca={cfg.ConnectionAwareEnabled} wantsAudio={wantsAudio}");
    }

    private static void RegisterHotkeys(IntPtr hwnd, ShortcutHostConfig cfg)
    {
        var mod = (uint)cfg.Modifier;
        TryRegister(hwnd, HotkeyAudio, mod, cfg.VkAudioToggle);
        TryRegister(hwnd, HotkeyPlay, mod, cfg.VkPlayPause);
        TryRegister(hwnd, HotkeyNext, mod, cfg.VkNext);
        TryRegister(hwnd, HotkeyPrev, mod, cfg.VkPrev);
        TryRegister(hwnd, HotkeyVolUp, mod, cfg.VkVolUp);
        TryRegister(hwnd, HotkeyVolDown, mod, cfg.VkVolDown);
        TryRegister(hwnd, HotkeyQuality, mod, cfg.VkQuality);
    }

    private static void TryRegister(IntPtr hwnd, int id, uint mod, int vk)
    {
        if (vk == 0) return;
        var ok = RegisterHotKey(hwnd, id, mod, (uint)vk);
        Debugger.Show($"[Host:{_cfg?.ScId}] RegisterHotKey id={id} mod={mod} vk=0x{vk:X2} ok={ok}");
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;

        var id = wParam.ToInt32();
        Debugger.Show($"[Host:{_cfg?.ScId}] WM_HOTKEY id={id}");
        handled = true;

        switch (id)
        {
            case HotkeyAudio: ToggleAudio(); break;
            case HotkeyPlay: SendMediaKey(0xB3); break;
            case HotkeyNext: SendMediaKey(0xB0); break;
            case HotkeyPrev: SendMediaKey(0xB1); break;
            case HotkeyVolUp: AdjustVolume(+0.05f); break;
            case HotkeyVolDown: AdjustVolume(-0.05f); break;
            case HotkeyQuality: OpenQualityPicker(); break;
        }

        return IntPtr.Zero;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private static void ToggleAudio()
    {
        if (_cfg == null) return;
        Debugger.Show($"[Host:{_cfg.ScId}] Audio toggle.");
        SharedAudioSession.Toggle(_cfg.DeviceSerial, _cfg.ScrcpyPath, _cfg.OriginalArgs);
    }

    private static void AdjustVolume(float delta)
    {
        var pid = SharedAudioSession.AudioProcessId;
        if (pid <= 0) return;
        if (!ScrcpyVolumeController.TryAdjustVolume(pid, delta))
            Debugger.Show($"[Host:{_cfg?.ScId}] Volume adjust: no audio session found.");
    }

    private static void SendMediaKey(int vk)
    {
        Debugger.Show($"[Host:{_cfg?.ScId}] Sending media key 0x{vk:X2}");
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        keybd_event((byte)vk, 0, 2, UIntPtr.Zero);
    }

    private static void OpenQualityPicker()
    {
        if (_cfg == null) return;
        Debugger.Show($"[Host:{_cfg.ScId}] Opening quality picker.");

        Application.Current.Dispatcher.Invoke(() =>
        {
            var picker = new QualityPickerWindow(_cfg.OriginalArgs) { Topmost = true };
            if (picker.ShowDialog() == true)
                RestartScrcpy(newArgs: picker.UpdatedArgs);
        });
    }

    // ── Scrcpy lifecycle ─────────────────────────────────────────────────────

    private static void CheckScrcpy()
    {
        if (_cfg?.ScrcpyProcess == null) return;
        if (!_cfg.ScrcpyProcess.HasExited) return;

        Debugger.Show($"[Host:{_cfg.ScId}] scrcpy exited. Shutting down.");
        _watchdogTimer?.Stop();
        Cleanup();
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    private static void OnConnectionSwitch(string newSerial)
    {
        if (_cfg == null) return;
        Debugger.Show($"[Host:{_cfg.ScId}] Connection switch -> '{newSerial}'");
        _cfg.DeviceSerial = newSerial;
        RestartScrcpy();
    }

    public static void RestartScrcpy(string[]? newArgs = null)
    {
        if (_cfg == null) return;

        try
        {
            if (!_cfg.ScrcpyProcess.HasExited)
                _cfg.ScrcpyProcess.Kill();
        }
        catch { }

        var baseArgs = newArgs ?? _cfg.OriginalArgs;
        var sanitized = ShortcutLauncher.SanitizeArgsPublic(baseArgs);

        // Video scrcpy never handles audio in SC mode.
        sanitized.RemoveAll(a =>
            a.Equals("--no-audio", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--audio-source", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--audio-codec", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--audio-bit-rate", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--audio-buffer", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--audio-codec-options", StringComparison.OrdinalIgnoreCase));
        sanitized.Add("--no-audio");

        var psi = new ProcessStartInfo
        {
            FileName = _cfg.ScrcpyPath,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(_cfg.DeviceSerial);
        foreach (var arg in sanitized)
            psi.ArgumentList.Add(arg);

        var newProcess = Process.Start(psi);
        Debugger.Show($"[Host:{_cfg.ScId}] Restarted scrcpy pid={newProcess?.Id}");

        _cfg.ScrcpyProcess = newProcess!;
        _cfg.OriginalArgs = baseArgs;
    }

    private static void Cleanup()
    {
        _connectionMonitor?.Dispose();
        _connectionMonitor = null;

        SharedAudioSession.UnregisterScInstance();

        if (_hwndSource == null) return;
        for (var i = HotkeyAudio; i <= HotkeyQuality; i++)
            UnregisterHotKey(_hwndSource.Handle, i);
        _hwndSource.RemoveHook(WndProc);
    }
}