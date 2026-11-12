using NetworkSpeed.Core.PerApp;

namespace NetworkSpeed.Core.Tests.PerApp;

public class PerAppReportBuilderTests
{
    private static Dictionary<ProcessIdentity, (ulong Sent, ulong Received)> Data(params (int Session, string Name, ulong Sent, ulong Received)[] rows) =>
        rows.ToDictionary(r => new ProcessIdentity(r.Session, r.Name), r => (r.Sent, r.Received));

    [Fact]
    public void Build_OnlyCallerSessionNamed_OthersInSystem()
    {
        var data = Data((1, "chrome", 100, 100), (2, "teams", 50, 50), (0, ProcessIdentity.SystemName, 10, 10));
        var rows = PerAppReportBuilder.Build(data, callerSessionId: 1, adapterSent: 160, adapterReceived: 160);
        Assert.Equal(["chrome", ProcessIdentity.SystemName], rows.Select(r => r.Name));
        Assert.DoesNotContain(rows, r => r.Name == "teams");
        var system = rows.Single(r => r.IsSystem);
        Assert.Equal(60UL, system.SentBytes);
    }

    [Fact]
    public void Build_ScalesToAdapterTotals_SumMatchesExactly()
    {
        var data = Data((1, "a", 1, 3), (1, "b", 1, 3), (1, "c", 1, 3));
        var rows = PerAppReportBuilder.Build(data, 1, adapterSent: 1000, adapterReceived: 10);
        Assert.Equal(1000UL, rows.Aggregate(0UL, (s, r) => s + r.SentBytes));
        Assert.Equal(10UL, rows.Aggregate(0UL, (s, r) => s + r.ReceivedBytes));
        Assert.All(rows, r => Assert.InRange(r.SentBytes, 333UL, 334UL));
    }

    [Fact]
    public void Build_ZeroEtwTotal_AllZeroNoDivideByZero()
    {
        var rows = PerAppReportBuilder.Build(Data((1, "a", 0, 0)), 1, 500, 500);
        Assert.All(rows, r => Assert.Equal((0UL, 0UL), (r.SentBytes, r.ReceivedBytes)));
    }

    [Fact]
    public void Build_SortedDescending()
    {
        var rows = PerAppReportBuilder.Build(Data((1, "small", 1, 1), (1, "big", 10, 10)), 1, 22, 22);
        Assert.Equal(["big", "small"], rows.Select(r => r.Name));
    }

    [Fact]
    public void Build_HugeValues_NoOverflow()
    {
        var rows = PerAppReportBuilder.Build(Data((1, "a", ulong.MaxValue / 2, 1)), 1, ulong.MaxValue / 2, 1);
        Assert.Equal(ulong.MaxValue / 2, Assert.Single(rows).SentBytes);
    }

    [Fact]
    public void Build_Empty_ReturnsEmpty() => Assert.Empty(PerAppReportBuilder.Build(Data(), 1, 100, 100));
}
