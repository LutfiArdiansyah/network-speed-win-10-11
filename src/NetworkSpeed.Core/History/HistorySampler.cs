using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.History;

/// <summary>
/// Loop riwayat service (spec H-05, H-06): tiap tick (1 detik) membaca counter, menjumlahkan selisih byte per menit,
/// menulis menit yang sudah lewat ke store tiap pergantian menit, dan membersihkan data kedaluwarsa sekali per hari
/// (termasuk saat start). Semua adapter kecuali loopback dicatat (pemilihan F-05 dilakukan App saat membaca).
/// Dipanggil dari satu thread.
/// </summary>
public sealed class HistorySampler(
    IInterfaceCounterSource source,
    TrafficAggregator aggregator,
    HistoryFlusher flusher,
    IHistoryStore store,
    TimeProvider time,
    IAppLog log)
{
    // Jeda antar tick melebihi ini dianggap sleep/resume (service tidak menerima event power seperti App): baseline baru
    // agar counter yang reset/melompat tidak tercatat sebagai lonjakan (spec E-05, E-07).
    private static readonly TimeSpan ResumeGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AdapterRefresh = TimeSpan.FromMinutes(1);

    private readonly CounterDeltaCalculator _deltas = new();
    private HashSet<Guid> _loopback = [];
    private DateTimeOffset? _adaptersReadAt;
    private DateTimeOffset? _lastTick;
    private string? _currentMinute;
    private string? _lastPurgeDay;

    public void Tick()
    {
        var now = time.GetUtcNow();
        var local = time.GetLocalNow().DateTime;
        if (_lastTick is { } last && now - last > ResumeGap)
        {
            _deltas.ResetBaseline();
        }

        _lastTick = now;
        RefreshAdapters(now);

        var counters = source.ReadCounters().Where(c => !_loopback.Contains(c.InterfaceId)).ToList();
        if (_deltas.Add(counters) is { } deltas)
        {
            aggregator.Add(deltas, local);
        }

        var minute = LocalTimeKey.Minute(local);
        if (_currentMinute is not null && minute != _currentMinute)
        {
            flusher.Enqueue(aggregator.DrainMinutesBefore(local), aggregator.DrainDays());
            flusher.TryFlush();
        }

        _currentMinute = minute;
        PurgeOncePerDay(local);
    }

    /// <summary>Resume diketahui secara eksplisit: baseline baru.</summary>
    public void OnResume() => _deltas.ResetBaseline();

    /// <summary>Saat service berhenti: tulis semua yang ada di memori, termasuk menit berjalan.</summary>
    public void Stop()
    {
        flusher.Enqueue(aggregator.DrainAllMinutes(), aggregator.DrainDays());
        flusher.TryFlush();
    }

    private void RefreshAdapters(DateTimeOffset now)
    {
        if (_adaptersReadAt is { } readAt && now - readAt < AdapterRefresh)
        {
            return;
        }

        _loopback = source.ReadAdapters().Where(a => a.Kind == AdapterKind.Loopback).Select(a => a.Id).ToHashSet();
        _adaptersReadAt = now;
    }

    private void PurgeOncePerDay(DateTime local)
    {
        var day = LocalTimeKey.Day(local);
        if (day == _lastPurgeDay)
        {
            return;
        }

        try
        {
            store.Purge(RetentionPolicy.MinuteCutoff(local), RetentionPolicy.DayCutoff(local));
            _lastPurgeDay = day;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Dicoba lagi pada tick berikutnya; pesan exception tidak dicatat.
            log.Warn($"History purge failed ({ex.GetType().Name}).");
        }
    }
}
