using NetworkSpeed.Core.Ipc;

namespace NetworkSpeed.Core.Tests.Ipc;

public class ReconnectBackoffTests
{
    [Fact]
    public void Backoff_Sequence_CapsAt30s()
    {
        var backoff = new ReconnectBackoff();
        Assert.Equal([1d, 2, 5, 10, 30, 30, 30], Enumerable.Range(0, 7).Select(_ => backoff.NextDelay().TotalSeconds));
    }

    [Fact]
    public void Backoff_Reset_StartsAgain()
    {
        var backoff = new ReconnectBackoff();
        backoff.NextDelay();
        backoff.NextDelay();
        backoff.Reset();
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }
}
