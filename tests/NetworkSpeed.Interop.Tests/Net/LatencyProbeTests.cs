using NetworkSpeed.Interop.Net;

namespace NetworkSpeed.Interop.Tests.Net;

public class LatencyProbeTests
{
    [Fact]
    public async Task LatencyProbe_Loopback_ReturnsRoundTrip()
    {
        var reading = await LatencyProbe.PingAsync("127.0.0.1", TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.NotNull(reading.RoundTrip);
    }

    [Fact]
    public async Task LatencyProbe_UnroutableHost_TimesOutNotThrows()
    {
        // 192.0.2.0/24 adalah TEST-NET-1 (RFC 5737): tidak pernah dirutekan.
        var reading = await LatencyProbe.PingAsync("192.0.2.1", TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.Null(reading.RoundTrip);
    }

    [Fact]
    public async Task LatencyProbe_UnresolvableHost_TimesOutNotThrows()
    {
        var reading = await LatencyProbe.PingAsync("does-not-exist.invalid", TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.Null(reading.RoundTrip);
    }
}
