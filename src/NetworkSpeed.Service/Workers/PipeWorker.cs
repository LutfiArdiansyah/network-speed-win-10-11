using Microsoft.Extensions.Hosting;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Service.Ipc;

namespace NetworkSpeed.Service.Workers;

/// <summary>Menjalankan named pipe read-only untuk App (spec S-04).</summary>
public sealed class PipeWorker(IPipeRequestHandler handler, IAppLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.Info("Pipe server starting.");
        try
        {
            await new PipeServer(PipeProtocol.PipeName, handler, log).RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service berhenti.
        }
    }
}
