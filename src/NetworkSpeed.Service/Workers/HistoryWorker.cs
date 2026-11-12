using Microsoft.Extensions.Hosting;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Service.Workers;

/// <summary>Menjalankan <see cref="HistorySampler"/> tiap 1 detik dan mem-flush semua data saat service berhenti (spec H-06).</summary>
public sealed class HistoryWorker(HistorySampler sampler, IAppLog log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    sampler.Tick();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Satu tick gagal tidak boleh menghentikan service; dicoba lagi di tick berikutnya.
                    log.Error("History tick failed.", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service berhenti.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        sampler.Stop();
        log.Info("History flushed on stop.");
    }
}
