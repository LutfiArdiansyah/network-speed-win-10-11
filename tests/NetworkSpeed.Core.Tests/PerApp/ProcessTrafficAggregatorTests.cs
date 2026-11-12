using NetworkSpeed.Core.PerApp;

namespace NetworkSpeed.Core.Tests.PerApp;

public class ProcessTrafficAggregatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData(10, TrafficDirection.Sent)]
    [InlineData(26, TrafficDirection.Sent)]
    [InlineData(42, TrafficDirection.Sent)]
    [InlineData(58, TrafficDirection.Sent)]
    [InlineData(11, TrafficDirection.Received)]
    [InlineData(27, TrafficDirection.Received)]
    [InlineData(43, TrafficDirection.Received)]
    [InlineData(59, TrafficDirection.Received)]
    public void DirectionOf_KnownIds(int id, TrafficDirection expected) => Assert.Equal(expected, KernelNetworkEvents.DirectionOf(id));

    [Theory]
    [InlineData(18)]
    [InlineData(34)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(12)]
    public void DirectionOf_CopiedRetransmitAndOthers_Null(int id) => Assert.Null(KernelNetworkEvents.DirectionOf(id));

    [Fact]
    public void Record_Loopback_Ignored()
    {
        var agg = new ProcessTrafficAggregator();
        agg.Record(10, 100, 500, isLoopback: true);
        agg.Fold(new Dictionary<int, ProcessIdentity> { [100] = new(1, "chrome") }, Today);
        Assert.Empty(agg.Session());
    }

    [Fact]
    public void Fold_KnownPid_AttributedToIdentity()
    {
        var agg = new ProcessTrafficAggregator();
        agg.Record(10, 100, 500, false);
        agg.Record(11, 100, 2000, false);
        agg.Fold(new Dictionary<int, ProcessIdentity> { [100] = new(1, "chrome") }, Today);
        Assert.Equal((500UL, 2000UL), agg.Session()[new ProcessIdentity(1, "chrome")]);
        Assert.Equal((500UL, 2000UL), agg.Today()[new ProcessIdentity(1, "chrome")]);
    }

    [Fact]
    public void Fold_UnknownPid_GoesToSystem()
    {
        var agg = new ProcessTrafficAggregator();
        agg.Record(42, 777, 300, false);
        agg.Fold(new Dictionary<int, ProcessIdentity>(), Today);
        Assert.Equal((300UL, 0UL), agg.Session()[ProcessIdentity.System]);
    }

    [Fact]
    public void Fold_Session0_GoesToSystem()
    {
        var agg = new ProcessTrafficAggregator();
        agg.Record(11, 4, 100, false);
        agg.Fold(new Dictionary<int, ProcessIdentity> { [4] = new(0, "svchost") }, Today);
        Assert.Equal((0UL, 100UL), Assert.Single(agg.Session()).Value);
        Assert.Equal(ProcessIdentity.System, Assert.Single(agg.Session()).Key);
    }

    [Fact]
    public void Fold_NewDay_ResetsToday_KeepsSession()
    {
        var agg = new ProcessTrafficAggregator();
        var snapshot = new Dictionary<int, ProcessIdentity> { [100] = new(1, "chrome") };
        agg.Record(10, 100, 10, false);
        agg.Fold(snapshot, Today);
        agg.Record(10, 100, 5, false);
        agg.Fold(snapshot, Today.AddDays(1));
        Assert.Equal(15UL, agg.Session()[new ProcessIdentity(1, "chrome")].Sent);
        Assert.Equal(5UL, agg.Today()[new ProcessIdentity(1, "chrome")].Sent);
    }

    [Fact]
    public void Record_NonPositiveSizeOrUnknownEvent_Ignored()
    {
        var agg = new ProcessTrafficAggregator();
        agg.Record(10, 100, 0, false);
        agg.Record(10, 100, -5, false);
        agg.Record(18, 100, 999, false);
        agg.Fold(new Dictionary<int, ProcessIdentity> { [100] = new(1, "chrome") }, Today);
        Assert.Empty(agg.Session());
    }

    [Fact]
    public async Task Record_ConcurrentThreads_NoLostBytes()
    {
        var agg = new ProcessTrafficAggregator();
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                agg.Record(10, 100 + (i % 4), 1, false);
            }
        }));
        await Task.WhenAll(tasks);
        var snapshot = Enumerable.Range(100, 4).ToDictionary(p => p, p => new ProcessIdentity(1, "p" + p));
        agg.Fold(snapshot, Today);
        Assert.Equal(80_000UL, (ulong)agg.Session().Values.Sum(v => (long)v.Sent));
    }

    [Fact]
    public void Fold_IdentityCap_OverflowToOther()
    {
        var agg = new ProcessTrafficAggregator(maxIdentities: 2);
        var snapshot = new Dictionary<int, ProcessIdentity> { [1] = new(1, "a"), [2] = new(1, "b"), [3] = new(1, "c") };
        agg.Record(10, 1, 1, false);
        agg.Record(10, 2, 1, false);
        agg.Record(10, 3, 1, false);
        agg.Fold(snapshot, Today);
        Assert.True(agg.Session().ContainsKey(new ProcessIdentity(1, ProcessIdentity.OtherName)));
        Assert.Equal(3UL, (ulong)agg.Session().Values.Sum(v => (long)v.Sent));
    }
}
