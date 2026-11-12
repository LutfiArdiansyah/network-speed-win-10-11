using System.Windows.Threading;
using NetworkSpeed.App.Composition;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Interop.Net;

namespace NetworkSpeed.App.Features;

/// <summary>Ping berkala (spec X-01) di luar UI thread; hasil diterbitkan di UI thread.</summary>
public sealed class LatencyMonitor : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly AppHost _host;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cancel = new();
    private bool _inFlight;

    public LatencyMonitor(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _timer = new DispatcherTimer();
        _timer.Tick += async (_, _) => await PingOnceAsync().ConfigureAwait(true);
        host.SettingsChanged += Apply;
    }

    public event Action<LatencyReading?>? Updated;

    public LatencyReading? Latest { get; private set; }

    public void Start() => Apply(_host.Settings);

    public void Dispose()
    {
        _timer.Stop();
        _cancel.Cancel();
        _cancel.Dispose();
        _host.SettingsChanged -= Apply;
    }

    private void Apply(UserSettings settings)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(settings.PingIntervalMs);
        if (settings.PingEnabled)
        {
            _timer.Start();
            return;
        }

        _timer.Stop();
        Latest = null;
        Updated?.Invoke(null);
    }

    private async Task PingOnceAsync()
    {
        if (_inFlight)
        {
            return;
        }

        _inFlight = true;
        try
        {
            Latest = await LatencyProbe.PingAsync(_host.Settings.PingHost, Timeout, _cancel.Token).ConfigureAwait(true);
            Updated?.Invoke(Latest);
        }
        catch (OperationCanceledException)
        {
            // Aplikasi sedang ditutup.
        }
        catch (InvalidOperationException ex)
        {
            _host.Log.Error("Ping failed.", ex);
        }
        finally
        {
            _inFlight = false;
        }
    }
}
