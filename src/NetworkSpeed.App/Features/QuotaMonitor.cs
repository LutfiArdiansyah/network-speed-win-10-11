using System.Globalization;
using System.IO;
using System.Windows.Threading;
using NetworkSpeed.App.Composition;
using NetworkSpeed.App.Tray;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Quota;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Storage;

namespace NetworkSpeed.App.Features;

/// <summary>
/// Kuota harian/bulanan (spec H-01): pemakaian per PC dari service (adapter pilihan user), ambang & notifikasi per user.
/// Dicek saat start, tiap 5 menit, dan saat ketersediaan service berubah. Notifikasi 80%/100% sekali per (periode, batas),
/// tercatat di <c>state.json</c> sehingga tidak berulang setelah App restart.
/// </summary>
public sealed class QuotaMonitor : IDisposable
{
    private const double BytesPerGb = 1024d * 1024 * 1024;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly AppHost _host;
    private readonly ServiceConnection _service;
    private readonly NotificationStateStore _state;
    private readonly TrayIconController _tray;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stop = new();
    private bool _checking;
    private bool _checkPending;
    private bool _disposed;

    public QuotaMonitor(AppHost host, ServiceConnection service, NotificationStateStore state, TrayIconController tray)
    {
        _host = host;
        _service = service;
        _state = state;
        _tray = tray;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await CheckAsync().ConfigureAwait(true);
        _service.StateChanged += OnServiceStateChanged;
        _host.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Ringkasan untuk tooltip; kosong bila tidak ada kuota aktif.</summary>
    public string Summary { get; private set; } = string.Empty;

    public void Start()
    {
        _timer.Start();
        _ = CheckAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _service.StateChanged -= OnServiceStateChanged;
        _host.SettingsChanged -= OnSettingsChanged;
        _stop.Cancel();
        _stop.Dispose();
    }

    public async Task CheckAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_checking)
        {
            // Settings/status berubah saat cek berjalan: cek lagi setelah selesai, jangan dibuang.
            _checkPending = true;
            return;
        }

        var settings = _host.Settings;
        if (settings.QuotaDailyGb is null && settings.QuotaMonthlyGb is null)
        {
            Summary = string.Empty;
            return;
        }

        _checking = true;
        try
        {
            if (!_service.IsAvailable)
            {
                Summary = "Quota: not available";
                return;
            }

            var selection = new AdapterSelection(settings.AdapterMode, settings.SpecificAdapterId, settings.IncludeVpnInAll);
            if (HistoryRequestFactory.AdapterIds(_service.Adapters, selection) is not { } ids)
            {
                Summary = "Quota: no adapter selected";
                return;
            }

            var now = DateTime.Now;
            var parts = new List<string>();
            if (settings.QuotaDailyGb is { } daily)
            {
                parts.Add(await EvaluateAsync(QuotaPeriod.Daily(now), daily, ids, "Today", "Daily").ConfigureAwait(true));
            }

            if (settings.QuotaMonthlyGb is { } monthly)
            {
                parts.Add(await EvaluateAsync(QuotaPeriod.Monthly(now, settings.QuotaMonthStartDay), monthly, ids, "Month", "Monthly").ConfigureAwait(true));
            }

            Summary = string.Join(" · ", parts);
        }
        catch (OperationCanceledException)
        {
            // App ditutup.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // state.json terkunci/tidak bisa ditulis: notifikasi akan dievaluasi lagi pada cek berikutnya.
            _host.Log.Error("Quota state could not be read or saved.", ex);
        }
        finally
        {
            _checking = false;
            if (_checkPending && !_disposed)
            {
                _checkPending = false;
                _ = _dispatcher.BeginInvoke(async () => await CheckAsync().ConfigureAwait(true));
            }
        }
    }

    private async Task<string> EvaluateAsync(QuotaWindow window, double limitGb, IReadOnlyList<Guid> ids, string label, string title)
    {
        var response = await _service.QueryAsync(HistoryRequestFactory.DailyTotals(ids, window.From, window.To), _stop.Token).ConfigureAwait(true);
        if (!response.Ok || response.History is null)
        {
            return $"{label}: not available";
        }

        var used = response.History.Aggregate(0UL, (s, p) => s + p.SentBytes + p.ReceivedBytes);
        var limit = (ulong)Math.Round(limitGb * BytesPerGb);
        var alerts = QuotaEvaluator.Evaluate(window, used, limit, _state.Load());
        foreach (var alert in alerts.Where(a => a.IsShown))
        {
            _tray.ShowBalloon(
                $"{title} data limit: {alert.Percent}% used",
                $"{TooltipBuilder.FormatBytes(used)} of {TooltipBuilder.FormatBytes(limit)} used.");
            _host.Log.Info(string.Create(CultureInfo.InvariantCulture, $"Quota alert shown: {window.Kind} {alert.Percent}%."));
        }

        if (alerts.Count > 0)
        {
            _state.MarkSent(alerts.Select(a => a.StateKey));
        }

        return $"{label}: {TooltipBuilder.FormatBytes(used)} / {TooltipBuilder.FormatBytes(limit)}";
    }

    private void OnServiceStateChanged() => _dispatcher.BeginInvoke(async () => await CheckAsync().ConfigureAwait(true));

    // Settings live-apply memicu event berkali-kali; cek ulang hanya bila nilai kuota berubah.
    private void OnSettingsChanged(Core.Settings.UserSettings s)
    {
        var key = (s.QuotaDailyGb, s.QuotaMonthlyGb, s.QuotaMonthStartDay, s.AdapterMode, s.SpecificAdapterId, s.IncludeVpnInAll);
        if (key == _lastQuotaSettings)
        {
            return;
        }

        _lastQuotaSettings = key;
        _ = CheckAsync();
    }

    private (double?, double?, int, Core.Adapters.AdapterSelectionMode, Guid?, bool) _lastQuotaSettings;
}
