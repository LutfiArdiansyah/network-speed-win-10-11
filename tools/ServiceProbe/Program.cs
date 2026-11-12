// Alat verifikasi manual service (bukan bagian produk): memanggil semua operasi pipe sebagai user biasa.
// Opsi --dev mengizinkan server di luar sesi 0 (service dijalankan sebagai konsol saat pengembangan).
using System.Globalization;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.PerApp;

var dev = args.Contains("--dev");
var client = new ServiceClient(PipeProtocol.PipeName, TimeSpan.FromSeconds(3), requireSessionZeroServer: !dev);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var status = await client.SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), cts.Token);
if (!status.Ok)
{
    Console.WriteLine($"Service unavailable ({status.ErrorCode}).");
    return 1;
}

Console.WriteLine($"Status: version={status.Status!.Version} uptime={status.Status.UptimeSeconds}s etw={status.Status.EtwAvailable}");

var adapters = (await client.SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetAdapters), cts.Token)).Adapters ?? [];
Console.WriteLine($"Adapters: {adapters.Count} ({string.Join(", ", adapters.Select(a => a.Kind))})");
var selected = AdapterSelector.Select(adapters, AdapterSelectionMode.Auto, null, false).ToList();
if (selected.Count > 0)
{
    var now = DateTime.Now;
    var history = await client.SendAsync(
        new PipeRequest(PipeProtocol.Version, PipeOperations.GetHistory)
        {
            AdapterIds = selected,
            From = LocalTimeKey.Minute(now.AddMinutes(-59)),
            To = LocalTimeKey.Minute(now),
            Resolution = HistoryResolution.Minute,
        },
        cts.Token);
    Console.WriteLine($"History (last 60 min, Auto adapters): {history.History?.Count ?? 0} point(s), ok={history.Ok} {history.ErrorCode}");
    foreach (var p in (history.History ?? []).TakeLast(args.Contains("--all") ? int.MaxValue : 5))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {p.LocalTime}  sent={p.SentBytes / 1024} KB  received={p.ReceivedBytes / 1024} KB"));
    }
}

var perApp = await client.SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp) { Period = PerAppPeriod.Session }, cts.Token);
Console.WriteLine($"Per-app (session): ok={perApp.Ok} {perApp.ErrorCode} rows={perApp.PerApp?.Count ?? 0}");
foreach (var row in (perApp.PerApp ?? []).Take(8))
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {(row.IsSystem ? "[System]" : row.Name),-24} sent={row.SentBytes / 1024} KB  received={row.ReceivedBytes / 1024} KB"));
}

if (perApp.PerApp is { Count: > 0 } rows)
{
    var sum = rows.Aggregate(0UL, (s, r) => s + r.SentBytes + r.ReceivedBytes);
    Console.WriteLine($"  (total {sum / 1024} KB, approximate)");
}

return 0;
