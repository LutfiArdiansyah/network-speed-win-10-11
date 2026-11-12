using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetworkSpeed.App.Composition;
using NetworkSpeed.App.Features;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.App.History;

/// <summary>
/// Riwayat ↑/↓ (spec H-02): Live dari buffer App (tanpa service), 24 jam per menit dan 7 hari per jam dari service.
/// Query async dengan pembatalan saat jendela ditutup; tanpa service tampil pesan, bukan error.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Siklus hidup jendela WPF: CTS di-dispose pada event Closed.")]
public partial class HistoryWindow : Window
{
    private const string NotAvailable = "History is not available (the Network Speed service is not running).";
    private const string NoAdapter = "No adapter selected for history.";
    private const string Loading = "Loading…";
    private static readonly TimeSpan LiveRefresh = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HistoryRefresh = TimeSpan.FromSeconds(60);

    private readonly AppHost _host;
    private readonly ServiceConnection _service;
    private readonly LiveHistoryBuffer _live;
    private readonly TrafficChart _chart = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer _timer = new();
    private readonly CancellationTokenSource _closing = new();
    private bool _loading;
    private bool _refreshPending;

    public HistoryWindow(AppHost host, ServiceConnection service, LiveHistoryBuffer live)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _service = service;
        _live = live;
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        Closed += (_, _) =>
        {
            _timer.Stop();
            _closing.Cancel();
            _closing.Dispose();
        };
        Loaded += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && IsLoaded)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        if (_loading)
        {
            // Mis. pindah tab saat query berjalan: jalankan lagi setelah yang ini selesai, jangan dibuang.
            _refreshPending = true;
            return;
        }

        if (Tabs.SelectedItem is TabItem tab && tab.Content != _chart)
        {
            foreach (TabItem other in Tabs.Items)
            {
                other.Content = null;
            }

            tab.Content = _chart;
        }

        _chart.UnitMode = _host.Settings.UnitMode;
        _loading = true;
        try
        {
            switch (Tabs.SelectedIndex)
            {
                case 0:
                    _timer.Interval = LiveRefresh;
                    ShowLive();
                    break;
                case 1:
                    _timer.Interval = HistoryRefresh;
                    await ShowHistoryAsync(TimeSpan.FromHours(24), perHour: false).ConfigureAwait(true);
                    break;
                default:
                    _timer.Interval = HistoryRefresh;
                    await ShowHistoryAsync(TimeSpan.FromDays(7), perHour: true).ConfigureAwait(true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Jendela ditutup saat query berjalan.
        }
        finally
        {
            _loading = false;
            if (!_closing.IsCancellationRequested)
            {
                _timer.Start();
                if (_refreshPending)
                {
                    _refreshPending = false;
                    _ = Dispatcher.BeginInvoke(async () => await RefreshAsync().ConfigureAwait(true));
                }
            }
        }
    }

    private void ShowLive()
    {
        var values = _live.Snapshot();
        _chart.SetValues(values);
        StatusText.Text = string.Empty;
        TotalsText.Text = values.Count == 0 ? string.Empty
            : $"Now: ↑ {SpeedFormatter.Format(values[^1].Up, _host.Settings.UnitMode)}  ↓ {SpeedFormatter.Format(values[^1].Down, _host.Settings.UnitMode)}";
    }

    private async Task ShowHistoryAsync(TimeSpan span, bool perHour)
    {
        if (!_service.FirstCheck.IsCompleted)
        {
            // Proses viewer baru start: tunggu status service pertama (maks ±timeout pipe), jangan langsung "not available".
            Clear(Loading);
            await _service.FirstCheck.WaitAsync(_closing.Token).ConfigureAwait(true);
        }

        if (!_service.IsAvailable)
        {
            Clear(NotAvailable);
            return;
        }

        var selection = new AdapterSelection(_host.Settings.AdapterMode, _host.Settings.SpecificAdapterId, _host.Settings.IncludeVpnInAll);
        if (HistoryRequestFactory.AdapterIds(_service.Adapters, selection) is not { } ids)
        {
            Clear(NoAdapter);
            return;
        }

        var now = DateTime.Now;
        var from = now - span + TimeSpan.FromMinutes(1);
        var tab = Tabs.SelectedIndex;
        var response = await _service.QueryAsync(HistoryRequestFactory.Minutes(ids, from, now), _closing.Token).ConfigureAwait(true);
        if (Tabs.SelectedIndex != tab)
        {
            // Tab sudah berganti selama query: hasil ini milik tab lain, jangan ditampilkan.
            _refreshPending = true;
            return;
        }

        if (!response.Ok || response.History is null)
        {
            Clear(response.ErrorCode == PipeErrors.Unavailable ? NotAvailable : "History could not be loaded. Try again later.");
            return;
        }

        var series = perHour ? HistorySeries.PerHour(response.History, from, now) : HistorySeries.PerMinute(response.History, from, now);
        var seconds = perHour ? 3600d : 60d;
        _chart.SetValues(series.Select(p => (p.SentBytes / seconds, p.ReceivedBytes / seconds)).ToList());
        var sent = series.Aggregate(0UL, (s, p) => s + p.SentBytes);
        var received = series.Aggregate(0UL, (s, p) => s + p.ReceivedBytes);
        StatusText.Text = string.Empty;
        TotalsText.Text = $"Total: ↑ {TooltipBuilder.FormatBytes(sent)}  ↓ {TooltipBuilder.FormatBytes(received)}";
    }

    private void Clear(string message)
    {
        _chart.SetValues([]);
        StatusText.Text = message;
        TotalsText.Text = string.Empty;
    }
}
