using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.History;

/// <summary>
/// Mengumpulkan selisih byte per detik menjadi agregat per menit dan per hari, dengan kunci <b>waktu lokal</b> (spec E-15):
/// menit yang terulang saat jam mundur dijumlahkan ke kunci yang sama. Thread-safe: loop sampling menulis,
/// handler pipe membaca snapshot menit berjalan.
/// </summary>
public sealed class TrafficAggregator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<MinuteKey, (ulong Sent, ulong Received)> _minutes = [];
    private readonly Dictionary<DayKey, (ulong Sent, ulong Received)> _days = [];

    public void Add(IReadOnlyList<ByteDelta> deltas, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        var minute = LocalTimeKey.Minute(localNow);
        var day = LocalTimeKey.Day(localNow);
        lock (_gate)
        {
            foreach (var d in deltas)
            {
                if (d.SentBytes == 0 && d.ReceivedBytes == 0)
                {
                    continue;
                }

                Accumulate(_minutes, new MinuteKey(d.InterfaceId, minute), d);
                Accumulate(_days, new DayKey(d.InterfaceId, day), d);
            }
        }
    }

    /// <summary>
    /// Mengambil semua menit selain menit berjalan. Memakai "bukan menit sekarang" (bukan "lebih kecil"), agar menit
    /// yang tampak lebih baru setelah jam mundur tidak tertahan di memori.
    /// </summary>
    public IReadOnlyList<TrafficBucket<MinuteKey>> DrainMinutesBefore(DateTime localNow)
    {
        var current = LocalTimeKey.Minute(localNow);
        lock (_gate)
        {
            return Drain(_minutes, k => k.LocalMinute != current);
        }
    }

    /// <summary>Semua menit termasuk yang berjalan, untuk flush saat service berhenti.</summary>
    public IReadOnlyList<TrafficBucket<MinuteKey>> DrainAllMinutes()
    {
        lock (_gate)
        {
            return Drain(_minutes, _ => true);
        }
    }

    /// <summary>Kenaikan total harian sejak drain terakhir (store menjumlahkannya ke baris yang ada).</summary>
    public IReadOnlyList<TrafficBucket<DayKey>> DrainDays()
    {
        lock (_gate)
        {
            return Drain(_days, _ => true);
        }
    }

    /// <summary>Salinan menit yang belum ditulis (mis. menit berjalan) tanpa menghapusnya.</summary>
    public IReadOnlyList<TrafficBucket<MinuteKey>> SnapshotMinutes()
    {
        lock (_gate)
        {
            return _minutes.Select(kv => new TrafficBucket<MinuteKey>(kv.Key, kv.Value.Sent, kv.Value.Received)).ToList();
        }
    }

    private static void Accumulate<TKey>(Dictionary<TKey, (ulong Sent, ulong Received)> map, TKey key, ByteDelta d)
        where TKey : notnull
    {
        map.TryGetValue(key, out var existing);
        map[key] = (existing.Sent + d.SentBytes, existing.Received + d.ReceivedBytes);
    }

    private static List<TrafficBucket<TKey>> Drain<TKey>(Dictionary<TKey, (ulong Sent, ulong Received)> map, Func<TKey, bool> include)
        where TKey : notnull
    {
        var drained = new List<TrafficBucket<TKey>>();
        foreach (var (key, value) in map.Where(kv => include(kv.Key)).ToList())
        {
            drained.Add(new TrafficBucket<TKey>(key, value.Sent, value.Received));
            map.Remove(key);
        }

        return drained;
    }
}
