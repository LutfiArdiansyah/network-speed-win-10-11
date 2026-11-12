using System.Collections.Concurrent;

namespace NetworkSpeed.Core.PerApp;

/// <summary>Arah trafik event Kernel-Network.</summary>
public enum TrafficDirection
{
    Sent,
    Received,
}

/// <summary>
/// ID event provider <c>Microsoft-Windows-Kernel-Network</c> yang dihitung (spec S-01, dari manifest):
/// kirim 10/26/42/58, terima 11/27/43/59. Event "copied on behalf of user" (18/34) dan retransmit (14/30) tidak
/// dihitung karena menduplikasi byte yang sama.
/// </summary>
public static class KernelNetworkEvents
{
    public static TrafficDirection? DirectionOf(int eventId) => eventId switch
    {
        10 or 26 or 42 or 58 => TrafficDirection.Sent,
        11 or 27 or 43 or 59 => TrafficDirection.Received,
        _ => null,
    };
}

/// <summary>Identitas aplikasi untuk laporan: sesi Windows + nama proses. Sesi 0/tidak diketahui → <see cref="System"/>.</summary>
public readonly record struct ProcessIdentity(int SessionId, string ProcessName)
{
    public const string SystemName = "System";
    public const string OtherName = "Other";

    public static ProcessIdentity System { get; } = new(0, SystemName);

    public bool IsSystem => SessionId == 0;
}

/// <summary>
/// Menjumlahkan byte per proses dari event ETW (spec S-01, S-02). <see cref="Record"/> dipanggil dari thread ETW
/// (O(1), tanpa alokasi per event); <see cref="Fold"/> dipanggil berkala untuk memetakan PID ke identitas.
/// Data hanya di memori: sesi berjalan (sejak service start) dan hari ini.
/// </summary>
public sealed class ProcessTrafficAggregator(int maxIdentities = 4096)
{
    private readonly ConcurrentDictionary<int, long[]> _perPid = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<ProcessIdentity, (ulong Sent, ulong Received)> _session = [];
    private readonly Dictionary<ProcessIdentity, (ulong Sent, ulong Received)> _today = [];
    private DateOnly? _todayDate;
    private DateTime? _coverageStart;

    /// <summary>
    /// Mencatat kapan sesi ETW mulai mengumpulkan data (waktu lokal). Hanya panggilan pertama yang dipakai, karena data
    /// "Session" diakumulasi sejak itu. Dipakai agar total adapter untuk skala S-09 mencakup jendela waktu yang sama.
    /// </summary>
    public void MarkCoverageStart(DateTime localNow)
    {
        lock (_gate)
        {
            _coverageStart ??= localNow;
        }
    }

    /// <summary>Awal jendela data per periode (lokal), atau <c>null</c> bila ETW belum pernah berjalan.</summary>
    public DateTime? CoverageStart(Ipc.PerAppPeriod period, DateTime localNow)
    {
        lock (_gate)
        {
            if (_coverageStart is not { } start)
            {
                return null;
            }

            return period == Ipc.PerAppPeriod.Session ? start : (start > localNow.Date ? start : localNow.Date);
        }
    }

    public void Record(int eventId, int pid, long size, bool isLoopback)
    {
        if (isLoopback || size <= 0 || KernelNetworkEvents.DirectionOf(eventId) is not { } direction)
        {
            return;
        }

        var slot = _perPid.GetOrAdd(pid, static _ => new long[2]);
        Interlocked.Add(ref slot[direction == TrafficDirection.Sent ? 0 : 1], size);
    }

    /// <summary>
    /// Memindahkan counter per PID ke identitas proses. PID yang tidak ada di snapshot (sudah keluar) atau milik sesi 0
    /// masuk ke "System" (fail closed: tidak pernah ditampilkan ke sesi user mana pun, spec S-05).
    /// </summary>
    public void Fold(IReadOnlyDictionary<int, ProcessIdentity> snapshot, DateOnly localToday)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_todayDate != localToday)
            {
                _today.Clear();
                _todayDate = localToday;
            }

            foreach (var (pid, slot) in _perPid)
            {
                var sent = (ulong)Interlocked.Exchange(ref slot[0], 0);
                var received = (ulong)Interlocked.Exchange(ref slot[1], 0);
                var known = snapshot.TryGetValue(pid, out var identity);
                if (!known)
                {
                    _perPid.TryRemove(pid, out _);
                }

                if (sent == 0 && received == 0)
                {
                    continue;
                }

                var target = Resolve(known && identity.SessionId != 0 ? identity : ProcessIdentity.System);
                Add(_session, target, sent, received);
                Add(_today, target, sent, received);
            }
        }
    }

    public IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> Session()
    {
        lock (_gate)
        {
            return new Dictionary<ProcessIdentity, (ulong, ulong)>(_session);
        }
    }

    public IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> Today()
    {
        lock (_gate)
        {
            return new Dictionary<ProcessIdentity, (ulong, ulong)>(_today);
        }
    }

    private static void Add(Dictionary<ProcessIdentity, (ulong Sent, ulong Received)> map, ProcessIdentity key, ulong sent, ulong received)
    {
        map.TryGetValue(key, out var existing);
        map[key] = (existing.Sent + sent, existing.Received + received);
    }

    // Batas jumlah identitas agar memori service tetap terbatas walau banyak proses berumur pendek.
    private ProcessIdentity Resolve(ProcessIdentity identity) =>
        _session.ContainsKey(identity) || _session.Count < maxIdentities
            ? identity
            : new ProcessIdentity(identity.SessionId, ProcessIdentity.OtherName);
}
