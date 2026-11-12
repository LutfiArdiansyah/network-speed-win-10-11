using System.Net;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using NetworkSpeed.Core.PerApp;

namespace NetworkSpeed.Service.Etw;

/// <summary>
/// Sesi ETW real-time untuk provider <c>Microsoft-Windows-Kernel-Network</c> (spec S-01), keyword IPv4+IPv6, level
/// Informational. Hasil spike: akun LocalService bawaan boleh mengaktifkan provider ini (S-03).
/// Hanya PID, ukuran, dan flag loopback yang diteruskan; alamat/port tidak disimpan (spec §7).
/// </summary>
public sealed class KernelNetworkEtwSource : IDisposable
{
    public const string SessionName = "NetworkSpeed-KernelNetwork";
    private const string ProviderName = "Microsoft-Windows-Kernel-Network";
    private const ulong Ipv4AndIpv6Keywords = 0x30;

    private TraceEventSession? _session;
    private Task? _pump;

    /// <summary>Sesi aktif dan pemompa event masih berjalan.</summary>
    public bool IsRunning => _session is not null && _pump is { IsCompleted: false };

    /// <exception cref="UnauthorizedAccessException">Akun tidak berhak mengaktifkan provider.</exception>
    public void Start(Action<int, int, long, bool> onEvent, Action<Exception>? onPumpFault = null)
    {
        ArgumentNullException.ThrowIfNull(onEvent);
        Stop();

        // Sesi dengan nama sama dari proses sebelumnya (crash) masih hidup di kernel; hentikan dulu.
        if (TraceEventSession.GetActiveSessionNames().Contains(SessionName))
        {
            using var stale = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
            stale.Stop();
        }

        var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        try
        {
            session.EnableProvider(ProviderName, TraceEventLevel.Informational, Ipv4AndIpv6Keywords);
            session.Source.Dynamic.All += e =>
            {
                var id = (int)e.ID;
                if (KernelNetworkEvents.DirectionOf(id) is null)
                {
                    return;
                }

                if (e.PayloadByName("PID") is not { } pid || e.PayloadByName("size") is not { } size)
                {
                    return;
                }

                var loopback = IsLoopback(e.PayloadByName("daddr")) || IsLoopback(e.PayloadByName("saddr"));
                onEvent(id, Convert.ToInt32(pid, System.Globalization.CultureInfo.InvariantCulture), Convert.ToInt64(size, System.Globalization.CultureInfo.InvariantCulture), loopback);
            };
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _session = session;
        _pump = Task.Factory.StartNew(() => session.Source.Process(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Kegagalan pemompa event dilaporkan (bukan diabaikan); IsRunning menjadi false sehingga worker memulai ulang.
        _pump.ContinueWith(t => onPumpFault?.Invoke(t.Exception!.GetBaseException()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Jumlah event yang hilang di buffer kernel (indikator beban).</summary>
    public int EventsLost => _session?.EventsLost ?? 0;

    public void Stop()
    {
        _session?.Dispose();
        _session = null;
        _pump = null;
    }

    public void Dispose() => Stop();

    private static bool IsLoopback(object? address) =>
        address is not null && IPAddress.TryParse(address.ToString(), out var ip) && IPAddress.IsLoopback(ip);
}
