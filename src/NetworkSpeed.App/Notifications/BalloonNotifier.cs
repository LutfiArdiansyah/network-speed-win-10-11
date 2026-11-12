using NetworkSpeed.App.Composition;
using NetworkSpeed.App.Tray;
using NetworkSpeed.Core.Connectivity;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Time;

namespace NetworkSpeed.App.Notifications;

/// <summary>Notifikasi koneksi putus/tersambung (spec X-02), diredam 5 detik agar Wi-Fi yang naik-turun tidak membanjiri.</summary>
public sealed class BalloonNotifier : IDisposable
{
    private static readonly TimeSpan StablePeriod = TimeSpan.FromSeconds(5);

    private readonly AppHost _host;
    private readonly TrayIconController _tray;
    private readonly ConnectivityDebouncer _debouncer = new(StablePeriod);
    private readonly StopwatchClock _clock = new();

    public BalloonNotifier(AppHost host, TrayIconController tray)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tray);
        _host = host;
        _tray = tray;
        host.ReadingUpdated += OnReading;
    }

    public void Dispose() => _host.ReadingUpdated -= OnReading;

    private void OnReading(SpeedReading? reading)
    {
        if (reading is null)
        {
            return;
        }

        var change = _debouncer.Update(!reading.IsOffline, _clock.Elapsed);
        if (change is null || !_host.Settings.ConnectivityNotifications)
        {
            return;
        }

        if (change == ConnectivityChange.WentOffline)
        {
            _tray.ShowBalloon("Connection lost", "No active network connection.");
        }
        else
        {
            _tray.ShowBalloon("Connection restored", "Network connection is back.");
        }

        _host.Log.Info($"Connectivity change notified: {change}.");
    }
}
