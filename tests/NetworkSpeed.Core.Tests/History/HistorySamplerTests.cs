using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Tests.Fakes;

namespace NetworkSpeed.Core.Tests.History;

public class HistorySamplerTests
{
    private static readonly Guid Wifi = new(1, 0, 0, new byte[8]);
    private static readonly Guid VEthernet = new(2, 0, 0, new byte[8]);
    private static readonly Guid Loop = new(3, 0, 0, new byte[8]);

    private sealed class Rig
    {
        public Rig()
        {
            Source.Adapters.Add(new AdapterInfo(Wifi, "Wi-Fi", AdapterKind.Wireless, true, true));
            Source.Adapters.Add(new AdapterInfo(VEthernet, "vEthernet", AdapterKind.Virtual, true, true));
            Source.Adapters.Add(new AdapterInfo(Loop, "Loopback", AdapterKind.Loopback, true, false));
            Source.Set(Wifi, 0, 0);
            Source.Set(VEthernet, 0, 0);
            Source.Set(Loop, 0, 0);
            Sampler = new HistorySampler(Source, new TrafficAggregator(), new HistoryFlusher(Store, new NullLog()), Store, Time, new NullLog());
        }

        public FakeCounterSource Source { get; } = new();

        public FakeHistoryStore Store { get; } = new();

        public FakeTime Time { get; } = new(new DateTimeOffset(2026, 11, 12, 10, 15, 0, TimeSpan.Zero));

        public HistorySampler Sampler { get; }

        public void TickAfter(double seconds)
        {
            Time.Advance(TimeSpan.FromSeconds(seconds));
            Sampler.Tick();
        }
    }

    [Fact]
    public void Sampler_TicksWithinMinute_NoWrite()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.Source.Set(Wifi, 100, 10);
        rig.TickAfter(1);
        rig.TickAfter(1);
        Assert.Empty(rig.Store.Minutes);
    }

    [Fact]
    public void Sampler_MinuteBoundary_WritesPreviousMinute()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.Source.Set(Wifi, 100, 10);
        rig.TickAfter(1);
        rig.TickAfter(59);
        var minute = Assert.Single(rig.Store.Minutes);
        Assert.Equal(new TrafficBucket<MinuteKey>(new MinuteKey(Wifi, "2026-11-12T10:15"), 10, 100), minute);
        Assert.Single(rig.Store.Days);
    }

    [Fact]
    public void Sampler_ResumeEvent_ResetsBaseline()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        rig.Source.Set(Wifi, 50_000_000, 0);
        rig.Sampler.Tick();
        rig.Source.Set(Wifi, 50_000_100, 0);
        rig.TickAfter(1);
        rig.Sampler.Stop();
        Assert.Equal(100UL, rig.Store.Minutes.Aggregate(0UL, (s, m) => s + m.ReceivedBytes));
    }

    [Fact]
    public void Sampler_ExplicitOnResume_ResetsBaseline()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.Sampler.OnResume();
        rig.Source.Set(Wifi, 9_999, 0);
        rig.TickAfter(1);
        rig.Sampler.Stop();
        Assert.Empty(rig.Store.Minutes);
    }

    [Fact]
    public void Sampler_Stop_FlushesCurrentMinute()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.Source.Set(Wifi, 7, 3);
        rig.TickAfter(1);
        rig.Sampler.Stop();
        Assert.Equal(7UL, Assert.Single(rig.Store.Minutes).ReceivedBytes);
    }

    [Fact]
    public void Sampler_DailyPurge_CalledOncePerDay()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.TickAfter(1);
        rig.TickAfter(3600);
        Assert.Single(rig.Store.Purges);
        rig.Time.Advance(TimeSpan.FromDays(1));
        rig.Sampler.Tick();
        Assert.Equal(2, rig.Store.Purges.Count);
        Assert.Equal(("2026-11-06T11:15", "2026-09-12"), rig.Store.Purges[1]);
    }

    [Fact]
    public void Sampler_RecordsVirtual_ExcludesLoopback()
    {
        var rig = new Rig();
        rig.Sampler.Tick();
        rig.Source.Set(VEthernet, 5, 5);
        rig.Source.Set(Loop, 999, 999);
        rig.TickAfter(1);
        rig.Sampler.Stop();
        Assert.Equal([VEthernet], rig.Store.Minutes.Select(m => m.Key.AdapterId).Distinct());
    }

    private sealed class NullLog : IAppLog
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
