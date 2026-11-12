// Host Windows Service NetworkSpeed (spec §3, S-03): berjalan sebagai LocalService sejak boot, mencatat riwayat per PC,
// mengumpulkan trafik per proses, dan melayani query read-only dari App lewat named pipe.
// Bisa juga dijalankan sebagai aplikasi konsol untuk pengembangan (UseWindowsService tidak aktif di luar SCM).
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.PerApp;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Interop;
using NetworkSpeed.Service.Ipc;
using NetworkSpeed.Service.Storage;
using NetworkSpeed.Service.Workers;

var dataDirectory = Environment.GetEnvironmentVariable("NETWORKSPEED_DATA_DIR") is { Length: > 0 } overrideDir
    ? overrideDir
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkSpeed");
var log = new RollingFileLog(Path.Combine(dataDirectory, "logs"));
var startedAt = TimeProvider.System.GetUtcNow();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "NetworkSpeedService");
builder.Logging.ClearProviders();

builder.Services.AddSingleton<IAppLog>(log);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IInterfaceCounterSource, IfTableCounterSource>();
builder.Services.AddSingleton<TrafficAggregator>();
builder.Services.AddSingleton<ProcessTrafficAggregator>(_ => new ProcessTrafficAggregator());
builder.Services.AddSingleton(_ => new SqliteHistoryStore(Path.Combine(dataDirectory, "history.db"), log));
builder.Services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<SqliteHistoryStore>());
builder.Services.AddSingleton(sp => new HistoryFlusher(sp.GetRequiredService<IHistoryStore>(), log));
builder.Services.AddSingleton(sp => new HistorySampler(
    sp.GetRequiredService<IInterfaceCounterSource>(),
    sp.GetRequiredService<TrafficAggregator>(),
    sp.GetRequiredService<HistoryFlusher>(),
    sp.GetRequiredService<IHistoryStore>(),
    TimeProvider.System,
    log));
builder.Services.AddSingleton<EtwWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EtwWorker>());
builder.Services.AddSingleton<IPipeRequestHandler>(sp => new PipeRequestHandler(
    sp.GetRequiredService<IHistoryStore>(),
    sp.GetRequiredService<TrafficAggregator>(),
    sp.GetRequiredService<ProcessTrafficAggregator>(),
    sp.GetRequiredService<IInterfaceCounterSource>(),
    () => sp.GetRequiredService<EtwWorker>().IsAvailable,
    TimeProvider.System,
    startedAt));
builder.Services.AddHostedService<AppExitSignalHost>();
builder.Services.AddHostedService<HistoryWorker>();
builder.Services.AddHostedService<PipeWorker>();

try
{
    log.Info("Service starting.");
    await builder.Build().RunAsync().ConfigureAwait(false);
    log.Info("Service stopped.");
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    log.Error("Service terminated unexpectedly.", ex);
    throw;
}
