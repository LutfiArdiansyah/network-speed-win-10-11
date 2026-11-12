using Microsoft.Extensions.Hosting;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.PerApp;
using NetworkSpeed.Service.Etw;
using NetworkSpeed.Service.Platform;

namespace NetworkSpeed.Service.Workers;

/// <summary>
/// Mengumpulkan trafik per proses lewat ETW (spec S-01) dan memetakan PID ke identitas tiap 5 detik. Sesi yang gagal
/// dibuka atau berhenti tiba-tiba dicoba lagi tiap 60 detik; selama itu per aplikasi melapor "unavailable".
/// Exception per siklus dicatat dan tidak menghentikan service.
/// </summary>
public sealed class EtwWorker(ProcessTrafficAggregator aggregator, IAppLog log, TimeProvider time) : BackgroundService
{
    private static readonly TimeSpan FoldInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);

    private readonly KernelNetworkEtwSource _source = new();

    public bool IsAvailable => _source.IsRunning;

    public override void Dispose()
    {
        _source.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextStartAttempt = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(FoldInterval);
        try
        {
            do
            {
                try
                {
                    if (!_source.IsRunning && time.GetUtcNow() >= nextStartAttempt)
                    {
                        TryStart();
                        nextStartAttempt = time.GetUtcNow() + RetryInterval;
                    }

                    aggregator.Fold(ProcessSnapshot.Read(), DateOnly.FromDateTime(time.GetLocalNow().DateTime));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    log.Error("Per-app cycle failed.", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service berhenti.
        }
        finally
        {
            _source.Stop();
        }
    }

    private void TryStart()
    {
        try
        {
            _source.Start(aggregator.Record, ex => log.Error("Per-app ETW event pump stopped; will restart.", ex));
            aggregator.MarkCoverageStart(time.GetLocalNow().DateTime);
            log.Info("Per-app ETW session started.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            log.Error("Per-app ETW session could not be started; retrying later.", ex);
        }
    }
}
