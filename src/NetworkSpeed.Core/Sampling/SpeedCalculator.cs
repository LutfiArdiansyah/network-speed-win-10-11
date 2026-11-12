namespace NetworkSpeed.Core.Sampling;

/// <summary>
/// Mengubah rangkaian snapshot counter kumulatif menjadi kecepatan per adapter.
/// Tidak thread-safe; satu instance dipakai oleh satu loop sampling.
/// </summary>
public sealed class SpeedCalculator
{
    private readonly CounterDeltaCalculator _deltas = new();
    private TimeSpan _baselineTimestamp;

    /// <summary>
    /// Menyimpan snapshot terbaru sebagai baseline dan mengembalikan kecepatan sejak baseline sebelumnya.
    /// </summary>
    /// <param name="snapshots">Counter semua adapter saat ini.</param>
    /// <param name="timestamp">Waktu dari monotonic clock, bukan jam dinding.</param>
    /// <returns>
    /// Kecepatan per adapter yang ada di kedua snapshot, atau <c>null</c> bila belum ada baseline
    /// atau waktu tidak maju. Adapter baru baru dihitung di panggilan berikutnya.
    /// </returns>
    public IReadOnlyDictionary<Guid, InterfaceRate>? AddSnapshot(
        IReadOnlyList<InterfaceCounterSnapshot> snapshots,
        TimeSpan timestamp)
    {
        var deltas = _deltas.Add(snapshots);
        var elapsed = timestamp - _baselineTimestamp;
        _baselineTimestamp = timestamp;

        if (deltas is null || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var seconds = elapsed.TotalSeconds;
        var rates = new Dictionary<Guid, InterfaceRate>(deltas.Count);
        foreach (var d in deltas)
        {
            rates[d.InterfaceId] = new InterfaceRate(d.InterfaceId, d.ReceivedBytes / seconds, d.SentBytes / seconds);
        }

        return rates;
    }

    /// <summary>Membuang baseline, mis. setelah resume dari sleep (spec E-05).</summary>
    public void ResetBaseline() => _deltas.ResetBaseline();
}
