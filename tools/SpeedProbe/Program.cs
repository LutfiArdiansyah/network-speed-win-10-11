// Alat verifikasi manual (bukan bagian produk): cetak kecepatan per detik untuk dibandingkan
// dengan Task Manager (spec §1, target selisih < 5%). Hanya mencetak jenis adapter, bukan nama/IP.
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Time;
using NetworkSpeed.Interop;

var seconds = args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed > 0 ? parsed : 60;
var source = new IfTableCounterSource();
var clock = new StopwatchClock();
var calculator = new SpeedCalculator();
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

Console.WriteLine($"SpeedProbe: {seconds} s, adapter mode Auto. Ctrl+C to stop.");
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
calculator.AddSnapshot(source.ReadCounters(), clock.Elapsed);
for (var i = 0; i < seconds && await WaitAsync(timer, cancel.Token); i++)
{
    var adapters = source.ReadAdapters();
    var selected = AdapterSelector.Select(adapters, AdapterSelectionMode.Auto, null, includeVpnInAll: false);
    var rates = calculator.AddSnapshot(source.ReadCounters(), clock.Elapsed);
    if (rates is null || selected.Count == 0)
    {
        Console.WriteLine(selected.Count == 0 ? "Offline" : "(baseline)");
        continue;
    }

    var down = selected.Sum(id => rates.TryGetValue(id, out var r) ? r.ReceivedBytesPerSecond : 0);
    var up = selected.Sum(id => rates.TryGetValue(id, out var r) ? r.SentBytesPerSecond : 0);
    var kinds = string.Join(",", adapters.Where(a => selected.Contains(a.Id)).Select(a => a.Kind));
    Console.WriteLine(
        $"{DateTime.Now:HH:mm:ss}  ↑ {SpeedFormatter.Format(up, SpeedUnitMode.BitsPerSecond),-10} ↓ {SpeedFormatter.Format(down, SpeedUnitMode.BitsPerSecond),-10} [{kinds}]");
}

static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
{
    try
    {
        return await timer.WaitForNextTickAsync(token);
    }
    catch (OperationCanceledException)
    {
        return false;
    }
}
