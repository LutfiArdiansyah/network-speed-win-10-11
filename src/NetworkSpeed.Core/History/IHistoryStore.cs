namespace NetworkSpeed.Core.History;

/// <summary>Satu titik riwayat: jumlah adapter yang diminta pada satu menit/hari lokal (spec H-07).</summary>
public sealed record HistoryPoint(string LocalTime, ulong SentBytes, ulong ReceivedBytes);

/// <summary>Penyimpanan riwayat per PC (spec H-05). Implementasi: SQLite di service.</summary>
public interface IHistoryStore
{
    /// <summary>Menambahkan (upsert-add) agregat dalam satu transaksi.</summary>
    void Write(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days);

    /// <summary>Menghapus baris dengan kunci lebih kecil dari cutoff (lihat <see cref="RetentionPolicy"/>).</summary>
    void Purge(string minuteCutoff, string dayCutoff);

    /// <summary>Jumlah per menit untuk adapter yang diminta, urut naik; rentang inklusif.</summary>
    IReadOnlyList<HistoryPoint> ReadMinutes(IReadOnlyCollection<Guid> adapters, string fromLocalMinute, string toLocalMinute);

    /// <summary>Jumlah per hari untuk adapter yang diminta, urut naik; rentang inklusif.</summary>
    IReadOnlyList<HistoryPoint> ReadDays(IReadOnlyCollection<Guid> adapters, string fromLocalDay, string toLocalDay);
}
