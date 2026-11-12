using System.Globalization;
using NetworkSpeed.Core.History;

namespace NetworkSpeed.Core.Display;

/// <summary>Satu titik grafik riwayat.</summary>
public sealed record SeriesPoint(DateTime LocalTime, ulong SentBytes, ulong ReceivedBytes);

/// <summary>
/// Mengubah titik riwayat dari service menjadi deret kontinu untuk grafik (spec H-02): setiap menit/jam di rentang
/// punya satu titik, celah (sleep, service mati) = 0. Titik dengan waktu tidak valid atau di luar rentang diabaikan.
/// </summary>
public static class HistorySeries
{
    public static IReadOnlyList<SeriesPoint> PerMinute(IReadOnlyList<HistoryPoint> points, DateTime fromLocal, DateTime toLocal) =>
        Build(points, Truncate(fromLocal, TimeSpan.FromMinutes(1)), Truncate(toLocal, TimeSpan.FromMinutes(1)), TimeSpan.FromMinutes(1));

    /// <summary>Menjumlahkan titik per menit menjadi per jam (mis. 7 hari = 168 titik).</summary>
    public static IReadOnlyList<SeriesPoint> PerHour(IReadOnlyList<HistoryPoint> points, DateTime fromLocal, DateTime toLocal) =>
        Build(points, Truncate(fromLocal, TimeSpan.FromHours(1)), Truncate(toLocal, TimeSpan.FromHours(1)), TimeSpan.FromHours(1));

    private static List<SeriesPoint> Build(IReadOnlyList<HistoryPoint> points, DateTime from, DateTime to, TimeSpan step)
    {
        ArgumentNullException.ThrowIfNull(points);
        var buckets = new Dictionary<DateTime, (ulong Sent, ulong Received)>();
        foreach (var point in points)
        {
            if (!DateTime.TryParseExact(point.LocalTime, LocalTimeKey.MinuteFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var bucket = Truncate(time, step);
            if (bucket < from || bucket > to)
            {
                continue;
            }

            buckets.TryGetValue(bucket, out var existing);
            buckets[bucket] = (existing.Sent + point.SentBytes, existing.Received + point.ReceivedBytes);
        }

        var series = new List<SeriesPoint>();
        for (var t = from; t <= to; t += step)
        {
            buckets.TryGetValue(t, out var value);
            series.Add(new SeriesPoint(t, value.Sent, value.Received));
        }

        return series;
    }

    private static DateTime Truncate(DateTime value, TimeSpan step) => new(value.Ticks - (value.Ticks % step.Ticks), value.Kind);
}
