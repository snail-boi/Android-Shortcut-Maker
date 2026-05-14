using System.Timers;

namespace android_shortcut_maker;

/// <summary>
/// Polls adb devices every second and notifies ShortcutHost when the
/// connection state changes between USB and Wireless Debugging.
/// Only active when Connection Aware Mode is enabled for the shortcut.
/// </summary>
internal sealed class ConnectionMonitor : IDisposable
{
    private readonly string _usbSerial;
    private readonly string _mdnsServiceName;
    private readonly Action<string> _onSwitchNeeded; // called with the new serial to use
    private readonly System.Timers.Timer _timer;

    private string _currentSerial;
    private bool _disposed;

    public ConnectionMonitor(
        string usbSerial,
        string mdnsServiceName,
        string initialSerial,
        Action<string> onSwitchNeeded)
    {
        _usbSerial       = usbSerial;
        _mdnsServiceName = mdnsServiceName;
        _currentSerial   = initialSerial;
        _onSwitchNeeded  = onSwitchNeeded;

        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTick;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        _timer.Start();
        Debugger.Show($"[ConnMonitor] Started. usb='{_usbSerial}' mdns='{_mdnsServiceName}' current='{_currentSerial}'");
    }

    public void Stop()
    {
        _timer.Stop();
        Debugger.Show("[ConnMonitor] Stopped.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }

    // ── Polling ──────────────────────────────────────────────────────────────

    private bool _tickRunning;

    private async void OnTick(object? sender, ElapsedEventArgs e)
    {
        // Prevent overlapping ticks if an adb call takes longer than 1 second.
        if (_tickRunning) return;
        _tickRunning = true;

        try
        {
            await CheckConnectionAsync();
        }
        catch (Exception ex)
        {
            Debugger.Show("[ConnMonitor] Tick error: " + ex.Message);
        }
        finally
        {
            _tickRunning = false;
        }
    }

    private async Task CheckConnectionAsync()
    {
        var output = await AdbHelper.RunAdbCaptureAsync("devices").ConfigureAwait(false);
        var lines  = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        bool IsOnline(string id) =>
            !string.IsNullOrWhiteSpace(id)
            && lines.Any(x =>
                x.StartsWith(id, StringComparison.OrdinalIgnoreCase)
                && x.TrimEnd().EndsWith("device", StringComparison.OrdinalIgnoreCase));

        var usbOnline = IsOnline(_usbSerial);
        var currentIsUsb = !_currentSerial.Contains(':');

        // Currently on WD and USB just appeared: switch to USB.
        if (!currentIsUsb && usbOnline)
        {
            Debugger.Show($"[ConnMonitor] USB appeared. Switching from WD '{_currentSerial}' to USB '{_usbSerial}'.");
            _currentSerial = _usbSerial;
            _onSwitchNeeded(_usbSerial);
            return;
        }

        // Currently on USB and it disappeared: switch to WD.
        if (currentIsUsb && !usbOnline)
        {
            Debugger.Show($"[ConnMonitor] USB lost. Trying WD reconnect via mDNS '{_mdnsServiceName}'.");

            var ipPort = string.Empty;
            if (!string.IsNullOrWhiteSpace(_mdnsServiceName))
                ipPort = await WirelessDebuggingHelper.ReconnectViaMdnsAsync(_mdnsServiceName).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(ipPort))
            {
                Debugger.Show($"[ConnMonitor] WD reconnected at '{ipPort}'. Switching.");
                _currentSerial = ipPort;
                _onSwitchNeeded(ipPort);
            }
            else
            {
                Debugger.Show("[ConnMonitor] WD reconnect failed. Will retry next tick.");
            }
        }
    }
}
