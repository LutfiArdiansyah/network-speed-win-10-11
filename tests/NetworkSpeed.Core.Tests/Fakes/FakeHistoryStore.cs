using NetworkSpeed.Core.History;

namespace NetworkSpeed.Core.Tests.Fakes;

internal sealed class FakeHistoryStore : IHistoryStore
{
    public List<TrafficBucket<MinuteKey>> Minutes { get; } = [];

    public List<TrafficBucket<DayKey>> Days { get; } = [];

    public List<(string Minute, string Day)> Purges { get; } = [];

    public void Write(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days)
    {
        Minutes.AddRange(minutes);
        Days.AddRange(days);
    }

    public void Purge(string minuteCutoff, string dayCutoff) => Purges.Add((minuteCutoff, dayCutoff));

    public IReadOnlyList<HistoryPoint> ReadMinutes(IReadOnlyCollection<Guid> adapters, string fromLocalMinute, string toLocalMinute) => [];

    public IReadOnlyList<HistoryPoint> ReadDays(IReadOnlyCollection<Guid> adapters, string fromLocalDay, string toLocalDay) => [];
}
