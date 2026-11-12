namespace NetworkSpeed.Core.Sampling;

/// <summary>Byte terkirim/diterima satu adapter sejak snapshot sebelumnya.</summary>
public readonly record struct ByteDelta(Guid InterfaceId, ulong SentBytes, ulong ReceivedBytes);

/// <summary>
/// Mengubah counter kumulatif menjadi selisih byte per adapter (dipakai riwayat service dan kecepatan overlay).
/// Counter yang turun (driver/adapter restart, resume) dianggap 0 (spec E-07). Tidak thread-safe.
/// </summary>
public sealed class CounterDeltaCalculator
{
    private Dictionary<Guid, InterfaceCounterSnapshot>? _baseline;

    /// <returns>
    /// Selisih untuk adapter yang ada di kedua snapshot, atau <c>null</c> bila belum ada baseline.
    /// Adapter baru baru dihitung di panggilan berikutnya.
    /// </returns>
    public IReadOnlyList<ByteDelta>? Add(IReadOnlyList<InterfaceCounterSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var previous = _baseline;
        _baseline = new Dictionary<Guid, InterfaceCounterSnapshot>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            _baseline[snapshot.InterfaceId] = snapshot;
        }

        if (previous is null)
        {
            return null;
        }

        var deltas = new List<ByteDelta>(_baseline.Count);
        foreach (var current in _baseline.Values)
        {
            if (previous.TryGetValue(current.InterfaceId, out var before))
            {
                deltas.Add(new ByteDelta(
                    current.InterfaceId,
                    NonNegative(current.BytesSent, before.BytesSent),
                    NonNegative(current.BytesReceived, before.BytesReceived)));
            }
        }

        return deltas;
    }

    /// <summary>Membuang baseline, mis. setelah resume dari sleep (spec E-05).</summary>
    public void ResetBaseline() => _baseline = null;

    private static ulong NonNegative(ulong current, ulong previous) => current >= previous ? current - previous : 0;
}
