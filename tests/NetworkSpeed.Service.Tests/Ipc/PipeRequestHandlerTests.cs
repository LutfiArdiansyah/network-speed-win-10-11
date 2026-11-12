using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.PerApp;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Service.Ipc;

namespace NetworkSpeed.Service.Tests.Ipc;

public class PipeRequestHandlerTests
{
    private static readonly Guid Wifi = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 15, 30, TimeSpan.Zero);

    private sealed class Rig
    {
        public FixedTime Time { get; } = new(Now);

        public StubStore Store { get; } = new();

        public TrafficAggregator Traffic { get; } = new();

        public ProcessTrafficAggregator PerApp { get; } = new();

        public bool EtwAvailable { get; set; } = true;

        public PipeRequestHandler Create() => new(Store, Traffic, PerApp, new StubSource(), () => EtwAvailable, Time, Now.AddHours(-1));
    }

    [Fact]
    public void GetHistory_IncludesCurrentMinuteFromMemory()
    {
        var rig = new Rig();
        rig.Store.MinutePoints.Add(new HistoryPoint("2026-11-12T10:14", 10, 100));
        rig.Traffic.Add([new ByteDelta(Wifi, 1, 5)], Now.DateTime);
        var request = new PipeRequest(PipeProtocol.Version, PipeOperations.GetHistory)
        {
            AdapterIds = [Wifi],
            From = "2026-11-12T10:00",
            To = "2026-11-12T10:59",
            Resolution = HistoryResolution.Minute,
        };
        var response = rig.Create().Handle(request, callerSessionId: 1);
        Assert.True(response.Ok);
        Assert.Equal([new HistoryPoint("2026-11-12T10:14", 10, 100), new HistoryPoint("2026-11-12T10:15", 1, 5)], response.History!);
    }

    [Fact]
    public void GetPerApp_UsesCallerSession()
    {
        var rig = new Rig();
        rig.PerApp.Record(10, 100, 500, false);
        rig.PerApp.Record(10, 200, 500, false);
        rig.PerApp.Fold(new Dictionary<int, ProcessIdentity> { [100] = new(1, "chrome"), [200] = new(2, "teams") }, DateOnly.FromDateTime(Now.DateTime));
        var response = rig.Create().Handle(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp) { Period = PerAppPeriod.Session }, callerSessionId: 1);
        Assert.True(response.Ok);
        Assert.Contains(response.PerApp!, r => r.Name == "chrome");
        Assert.DoesNotContain(response.PerApp!, r => r.Name == "teams");
        Assert.Contains(response.PerApp!, r => r.IsSystem);
    }

    [Fact]
    public void GetPerApp_Today_AfterMidDayRestart_ScalesToEtwCoverageWindow()
    {
        var rig = new Rig();
        rig.Store.MinutePoints.Add(new HistoryPoint("2026-11-12T08:00", 1000, 0)); // sebelum service restart
        rig.Store.MinutePoints.Add(new HistoryPoint("2026-11-12T10:10", 100, 0));  // setelah ETW mulai
        rig.PerApp.MarkCoverageStart(new DateTime(2026, 11, 12, 10, 0, 0));
        rig.PerApp.Record(10, 100, 50, false);
        rig.PerApp.Fold(new Dictionary<int, ProcessIdentity> { [100] = new(1, "chrome") }, DateOnly.FromDateTime(Now.DateTime));

        var response = rig.Create().Handle(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp) { Period = PerAppPeriod.Today }, 1);

        Assert.Equal(100UL, Assert.Single(response.PerApp!).SentBytes);
    }

    [Fact]
    public void GetPerApp_EtwUnavailable_ErrorCodeUnavailable()
    {
        var rig = new Rig { EtwAvailable = false };
        var response = rig.Create().Handle(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp) { Period = PerAppPeriod.Today }, 1);
        Assert.Equal(PipeErrors.Unavailable, response.ErrorCode);
    }

    [Fact]
    public void GetStatus_ReportsUptimeAndEtw()
    {
        var response = new Rig().Create().Handle(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), 1);
        Assert.Equal(3600, response.Status!.UptimeSeconds);
        Assert.True(response.Status.EtwAvailable);
    }

    [Fact]
    public void GetAdapters_ExcludesLoopback()
    {
        var response = new Rig().Create().Handle(new PipeRequest(PipeProtocol.Version, PipeOperations.GetAdapters), 1);
        Assert.Equal([Wifi], response.Adapters!.Select(a => a.Id));
    }

    internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class StubStore : IHistoryStore
    {
        public List<HistoryPoint> MinutePoints { get; } = [];

        public void Write(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days)
        {
        }

        public void Purge(string minuteCutoff, string dayCutoff)
        {
        }

        public IReadOnlyList<HistoryPoint> ReadMinutes(IReadOnlyCollection<Guid> adapters, string fromLocalMinute, string toLocalMinute) =>
            MinutePoints.Where(p => string.CompareOrdinal(p.LocalTime, fromLocalMinute) >= 0 && string.CompareOrdinal(p.LocalTime, toLocalMinute) <= 0).ToList();

        public IReadOnlyList<HistoryPoint> ReadDays(IReadOnlyCollection<Guid> adapters, string fromLocalDay, string toLocalDay) => [];
    }

    private sealed class StubSource : IInterfaceCounterSource
    {
        public IReadOnlyList<InterfaceCounterSnapshot> ReadCounters() => [];

        public IReadOnlyList<AdapterInfo> ReadAdapters() =>
        [
            new(Wifi, "Wi-Fi", AdapterKind.Wireless, true, true),
            new(Guid.NewGuid(), "Loopback", AdapterKind.Loopback, true, false),
        ];
    }
}
