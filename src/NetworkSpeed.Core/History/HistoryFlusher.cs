using System.Globalization;
using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Core.History;

/// <summary>
/// Menulis agregat ke store dan menahannya di memori bila penulisan gagal (disk penuh, DB terkunci), lalu mencoba lagi
/// pada flush berikutnya. Data menit yang tertunda dibatasi (default 1440 menit = 24 jam); yang tertua dibuang.
/// Dipanggil dari satu thread (loop riwayat).
/// </summary>
public sealed class HistoryFlusher(IHistoryStore store, IAppLog log, int maxPendingMinutes = 1440)
{
    private readonly List<TrafficBucket<MinuteKey>> _pendingMinutes = [];
    // Total harian digabung per kunci: memori tetap terbatas (hari × adapter) walau DB gagal berjam-jam.
    private readonly Dictionary<DayKey, (ulong Sent, ulong Received)> _pendingDays = [];

    public int PendingDayCount => _pendingDays.Count;

    public int PendingMinuteCount => _pendingMinutes.Select(m => m.Key.LocalMinute).Distinct(StringComparer.Ordinal).Count();

    public void Enqueue(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ArgumentNullException.ThrowIfNull(days);
        _pendingMinutes.AddRange(minutes);
        foreach (var day in days)
        {
            _pendingDays.TryGetValue(day.Key, out var existing);
            _pendingDays[day.Key] = (existing.Sent + day.SentBytes, existing.Received + day.ReceivedBytes);
        }

        EnforceCap();
    }

    /// <returns><c>true</c> bila tidak ada lagi yang tertunda.</returns>
    public bool TryFlush()
    {
        if (_pendingMinutes.Count == 0 && _pendingDays.Count == 0)
        {
            return true;
        }

        try
        {
            store.Write(_pendingMinutes, _pendingDays.Select(d => new TrafficBucket<DayKey>(d.Key, d.Value.Sent, d.Value.Received)).ToList());
            _pendingMinutes.Clear();
            _pendingDays.Clear();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Pesan exception tidak dicatat (bisa memuat path); data tetap tertunda untuk dicoba lagi.
            log.Warn(string.Create(CultureInfo.InvariantCulture, $"History write failed ({ex.GetType().Name}); {PendingMinuteCount} minute(s) kept for retry."));
            return false;
        }
    }

    private void EnforceCap()
    {
        var minutes = _pendingMinutes.Select(m => m.Key.LocalMinute).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var excess = minutes.Count - maxPendingMinutes;
        if (excess <= 0)
        {
            return;
        }

        var dropped = minutes.Take(excess).ToHashSet(StringComparer.Ordinal);
        _pendingMinutes.RemoveAll(m => dropped.Contains(m.Key.LocalMinute));
        log.Warn(string.Create(CultureInfo.InvariantCulture, $"History buffer full; dropped {excess} oldest pending minute(s)."));
    }
}
