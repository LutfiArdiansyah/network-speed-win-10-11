using NetworkSpeed.Core.Connectivity;

namespace NetworkSpeed.Core.Tests.Connectivity;

public class ConnectivityDebouncerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static ConnectivityDebouncer Create() => new(S(5));

    [Fact]
    public void Update_FirstObservation_NoEvent()
    {
        Assert.Null(Create().Update(false, S(0)));
    }

    [Fact]
    public void Update_FlapShorterThanStable_NoEvent()
    {
        var d = Create();
        d.Update(true, S(0));
        Assert.Null(d.Update(false, S(1)));
        Assert.Null(d.Update(false, S(3)));
        Assert.Null(d.Update(true, S(4)));
        Assert.Null(d.Update(true, S(20)));
    }

    [Fact]
    public void Update_StableOffline_EmitsOnce()
    {
        var d = Create();
        d.Update(true, S(0));
        Assert.Null(d.Update(false, S(1)));
        Assert.Equal(ConnectivityChange.WentOffline, d.Update(false, S(6)));
        Assert.Null(d.Update(false, S(7)));
        Assert.Null(d.Update(false, S(60)));
    }

    [Fact]
    public void Update_OfflineThenOnline_EmitsBoth()
    {
        var d = Create();
        d.Update(true, S(0));
        d.Update(false, S(1));
        Assert.Equal(ConnectivityChange.WentOffline, d.Update(false, S(6)));
        Assert.Null(d.Update(true, S(10)));
        Assert.Equal(ConnectivityChange.CameOnline, d.Update(true, S(15)));
    }
}
