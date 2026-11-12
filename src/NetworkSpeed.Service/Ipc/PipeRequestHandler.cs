using System.Reflection;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.PerApp;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Service.Ipc;

/// <summary>
/// Menjawab query App (spec H-07, S-02, S-05, S-09). Request sudah divalidasi oleh <see cref="PipeServer"/>.
/// Riwayat = data di store + menit yang masih di memori (belum ditulis), sehingga App melihat data terbaru.
/// </summary>
public sealed class PipeRequestHandler(
    IHistoryStore store,
    TrafficAggregator traffic,
    ProcessTrafficAggregator perApp,
    IInterfaceCounterSource adapters,
    Func<bool> etwAvailable,
    TimeProvider time,
    DateTimeOffset startedAt) : IPipeRequestHandler
{
    private static readonly TimeSpan AdapterCacheLifetime = TimeSpan.FromSeconds(10);
    private static readonly string Version =
        typeof(PipeRequestHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private readonly Lock _gate = new();
    private IReadOnlyList<AdapterInfo>? _adapters;
    private DateTimeOffset _adaptersReadAt;

    public PipeResponse Handle(PipeRequest request, int callerSessionId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Operation switch
        {
            PipeOperations.GetStatus => PipeResponse.Success() with
            {
                Status = new ServiceStatus(Version, (long)(time.GetUtcNow() - startedAt).TotalSeconds, etwAvailable()),
            },
            PipeOperations.GetAdapters => PipeResponse.Success() with { Adapters = Adapters().Where(a => a.Kind != AdapterKind.Loopback).ToList() },
            PipeOperations.GetHistory => PipeResponse.Success() with { History = History(request.AdapterIds!, request.From!, request.To!, request.Resolution!.Value) },
            PipeOperations.GetDailyTotals => PipeResponse.Success() with { History = History(request.AdapterIds!, request.From!, request.To!, HistoryResolution.Day) },
            PipeOperations.GetPerApp => PerApp(request.Period!.Value, callerSessionId),
            _ => PipeResponse.Error(PipeErrors.InvalidRequest),
        };
    }

    private List<HistoryPoint> History(IReadOnlyList<Guid> ids, string from, string to, HistoryResolution resolution)
    {
        var stored = resolution == HistoryResolution.Minute ? store.ReadMinutes(ids, from, to) : store.ReadDays(ids, from, to);
        var wanted = ids.ToHashSet();
        var keyLength = resolution == HistoryResolution.Minute ? LocalTimeKey.MinuteKeyLength : LocalTimeKey.DayKeyLength;
        var pending = traffic.SnapshotMinutes()
            .Where(m => wanted.Contains(m.Key.AdapterId))
            .Select(m => new HistoryPoint(m.Key.LocalMinute[..keyLength], m.SentBytes, m.ReceivedBytes))
            .Where(p => string.CompareOrdinal(p.LocalTime, from) >= 0 && string.CompareOrdinal(p.LocalTime, to) <= 0);

        return stored.Concat(pending)
            .GroupBy(p => p.LocalTime, StringComparer.Ordinal)
            .Select(g => new HistoryPoint(g.Key, g.Aggregate(0UL, (s, p) => s + p.SentBytes), g.Aggregate(0UL, (s, p) => s + p.ReceivedBytes)))
            .OrderBy(p => p.LocalTime, StringComparer.Ordinal)
            .ToList();
    }

    private PipeResponse PerApp(PerAppPeriod period, int callerSessionId)
    {
        if (!etwAvailable())
        {
            return PipeResponse.Error(PipeErrors.Unavailable);
        }

        var data = period == PerAppPeriod.Session ? perApp.Session() : perApp.Today();
        var (adapterSent, adapterReceived) = AdapterTotals(period, data);
        return PipeResponse.Success() with { PerApp = PerAppReportBuilder.Build(data, callerSessionId, adapterSent, adapterReceived) };
    }

    // Skala memakai adapter mode Auto (default overlay) agar trafik tidak terhitung ganda (mis. vEthernet + NIC fisik).
    // Tanpa adapter terpilih, total ETW dipakai apa adanya.
    private (ulong Sent, ulong Received) AdapterTotals(PerAppPeriod period, IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> data)
    {
        var ids = AdapterSelector.Select(Adapters(), AdapterSelectionMode.Auto, null, includeVpnInAll: false).ToList();
        if (ids.Count == 0)
        {
            return (data.Values.Aggregate(0UL, (s, v) => s + v.Sent), data.Values.Aggregate(0UL, (s, v) => s + v.Received));
        }

        // Jendela total adapter = jendela data ETW (sejak sesi ETW mulai; untuk "Today" tidak lebih awal dari tengah malam),
        // agar restart service di tengah hari tidak membuat angka per aplikasi membengkak.
        var now = time.GetLocalNow().DateTime;
        if (perApp.CoverageStart(period, now) is not { } from)
        {
            return (data.Values.Aggregate(0UL, (s, v) => s + v.Sent), data.Values.Aggregate(0UL, (s, v) => s + v.Received));
        }

        // Data menit hanya disimpan 7 hari; jendela yang lebih tua memakai total harian (hari pertama dihitung penuh).
        var points = from >= now.AddDays(-RetentionPolicy.MinuteDays)
            ? History(ids, LocalTimeKey.Minute(from), LocalTimeKey.Minute(now), HistoryResolution.Minute)
            : History(ids, LocalTimeKey.Day(from), LocalTimeKey.Day(now), HistoryResolution.Day);
        return (points.Aggregate(0UL, (s, p) => s + p.SentBytes), points.Aggregate(0UL, (s, p) => s + p.ReceivedBytes));
    }

    private IReadOnlyList<AdapterInfo> Adapters()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_adapters is null || now - _adaptersReadAt > AdapterCacheLifetime)
            {
                _adapters = adapters.ReadAdapters();
                _adaptersReadAt = now;
            }

            return _adapters;
        }
    }
}
