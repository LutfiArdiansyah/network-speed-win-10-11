using System.Windows;
using System.Windows.Threading;
using NetworkSpeed.App.Features;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Ipc;

namespace NetworkSpeed.App.PerApp;

/// <summary>
/// Pemakaian per aplikasi milik sesi user ini + baris "System" (spec S-02, S-05, S-09). Refresh tiap 5 detik selama
/// terbuka; tanpa service/ETW tampil pesan. Nama proses hanya ditampilkan, tidak dicatat ke log.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Siklus hidup jendela WPF: CTS di-dispose pada event Closed.")]
public partial class PerAppWindow : Window
{
    private const string SystemLabel = "System (Windows services and other users)";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly ServiceConnection _service;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly CancellationTokenSource _closing = new();
    private bool _loading;

    public PerAppWindow(ServiceConnection service)
    {
        _service = service;
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        Loaded += async (_, _) =>
        {
            await RefreshAsync().ConfigureAwait(true);
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _closing.Cancel();
            _closing.Dispose();
        };
    }

    private async void OnPeriodChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync()
    {
        if (_loading || _closing.IsCancellationRequested)
        {
            return;
        }

        _loading = true;
        try
        {
            if (!_service.FirstCheck.IsCompleted)
            {
                // Proses viewer baru start: tunggu status service pertama, jangan langsung "not available".
                Show([], "Loading…");
                await _service.FirstCheck.WaitAsync(_closing.Token).ConfigureAwait(true);
            }

            if (!_service.IsAvailable)
            {
                Show([], "Per-app usage is not available (the Network Speed service is not running).");
                return;
            }

            var period = TodayRadio.IsChecked == true ? PerAppPeriod.Today : PerAppPeriod.Session;
            var response = await _service.QueryAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp) { Period = period }, _closing.Token).ConfigureAwait(true);
            if (!response.Ok || response.PerApp is null)
            {
                Show([], "Per-app usage is not available right now.");
                return;
            }

            Show(response.PerApp.Select(r => new Row(
                r.IsSystem ? SystemLabel : r.Name,
                TooltipBuilder.FormatBytes(r.SentBytes),
                TooltipBuilder.FormatBytes(r.ReceivedBytes),
                TooltipBuilder.FormatBytes(r.SentBytes + r.ReceivedBytes))).ToList(), response.PerApp.Count == 0 ? "No traffic recorded yet." : string.Empty);
        }
        catch (OperationCanceledException)
        {
            // Jendela ditutup.
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show(IReadOnlyList<Row> rows, string status)
    {
        Rows.ItemsSource = rows;
        StatusText.Text = status;
    }

    /// <summary>Baris tampilan (sudah diformat).</summary>
    public sealed record Row(string Name, string Sent, string Received, string Total);
}
