using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.Ipc;

/// <summary>Menyusun request riwayat/kuota dari pilihan adapter user (spec F-04, H-07).</summary>
public static class HistoryRequestFactory
{
    /// <summary>
    /// ID adapter pilihan user dari daftar adapter <b>service</b>. <c>null</c> bila tidak ada yang cocok
    /// (UI: "No adapter selected"). Maksimal 64 ID (batas protokol), diurutkan agar deterministik.
    /// </summary>
    public static IReadOnlyList<Guid>? AdapterIds(IReadOnlyList<AdapterInfo> serviceAdapters, AdapterSelection selection)
    {
        ArgumentNullException.ThrowIfNull(serviceAdapters);
        ArgumentNullException.ThrowIfNull(selection);
        var ids = AdapterSelector.Select(serviceAdapters, selection.Mode, selection.SpecificId, selection.IncludeVpnInAll)
            .Order()
            .Take(PipeProtocol.MaxAdapterIds)
            .ToList();
        return ids.Count == 0 ? null : ids;
    }

    public static PipeRequest Minutes(IReadOnlyList<Guid> ids, DateTime fromLocal, DateTime toLocal) =>
        new(PipeProtocol.Version, PipeOperations.GetHistory)
        {
            AdapterIds = ids,
            From = LocalTimeKey.Minute(fromLocal),
            To = LocalTimeKey.Minute(toLocal),
            Resolution = HistoryResolution.Minute,
        };

    public static PipeRequest DailyTotals(IReadOnlyList<Guid> ids, DateOnly from, DateOnly to) =>
        new(PipeProtocol.Version, PipeOperations.GetDailyTotals)
        {
            AdapterIds = ids,
            From = LocalTimeKey.Day(from.ToDateTime(TimeOnly.MinValue)),
            To = LocalTimeKey.Day(to.ToDateTime(TimeOnly.MinValue)),
        };
}
