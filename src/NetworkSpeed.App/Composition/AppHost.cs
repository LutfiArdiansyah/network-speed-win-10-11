using System.Net.NetworkInformation;
using System.Windows.Threading;
using Microsoft.Win32;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Storage;
using NetworkSpeed.Core.Time;
using NetworkSpeed.Interop;

namespace NetworkSpeed.App.Composition;

/// <summary>
/// Pusat state App: pengaturan, loop sampling, dan event power/jaringan. Semua anggota dipanggil dari UI thread.
/// </summary>
public sealed class AppHost : IDisposable
{
    private static readonly TimeSpan AdapterRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly SettingsFileStore _store;
    private readonly bool _canSaveSettings;
    private readonly SpeedSampler _sampler;
    private readonly DispatcherTimer _sampleTimer;
    private readonly Dispatcher _dispatcher;

    public AppHost(IAppLog log, SettingsFileStore store, UserSettings settings, bool canSaveSettings)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        Log = log;
        _store = store;
        _canSaveSettings = canSaveSettings;
        Settings = SettingsValidator.Normalize(settings);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _sampler = new SpeedSampler(new IfTableCounterSource(), new StopwatchClock(), AdapterRefreshInterval);
        _sampleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(Settings.RefreshIntervalMs) };
        _sampleTimer.Tick += (_, _) => SampleNow();
    }

    public event Action<UserSettings>? SettingsChanged;

    public event Action<SpeedReading?>? ReadingUpdated;

    /// <summary>Dipicu setelah resume dari sleep (posisi taskbar perlu dihitung ulang).</summary>
    public event Action? Resumed;

    public IAppLog Log { get; }

    public UserSettings Settings { get; private set; }

    public SpeedReading? LastReading { get; private set; }

    public void Start()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SampleNow();
        _sampleTimer.Start();
    }

    /// <summary>Menerapkan pengaturan baru (dinormalisasi); disimpan ke disk bila <paramref name="persist"/> dan diizinkan.</summary>
    public void UpdateSettings(UserSettings settings, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = SettingsValidator.Normalize(settings);
        _sampleTimer.Interval = TimeSpan.FromMilliseconds(Settings.RefreshIntervalMs);
        if (persist && _canSaveSettings)
        {
            try
            {
                _store.Save(Settings);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Log.Error("Saving settings failed.", ex);
            }
        }

        SettingsChanged?.Invoke(Settings);
    }

    public void Dispose()
    {
        _sampleTimer.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void SampleNow()
    {
        var selection = new AdapterSelection(Settings.AdapterMode, Settings.SpecificAdapterId, Settings.IncludeVpnInAll);
        try
        {
            LastReading = _sampler.Sample(selection);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Reading network counters failed.", ex);
            LastReading = null;
        }

        ReadingUpdated?.Invoke(LastReading);
    }

    // Event NetworkChange datang dari thread lain.
    private void OnNetworkChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(_sampler.InvalidateAdapters);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            _sampler.OnResume();
            Log.Info("Resumed from sleep.");
            Resumed?.Invoke();
        });
    }
}
