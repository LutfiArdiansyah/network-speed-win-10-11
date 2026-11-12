using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.Tests.Sampling;

public class SpeedCalculatorTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static InterfaceCounterSnapshot S(Guid id, ulong rx, ulong tx) => new(id, rx, tx);

    private static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void AddSnapshot_FirstSample_ReturnsNull()
    {
        var calc = new SpeedCalculator();
        Assert.Null(calc.AddSnapshot([S(A, 100, 50)], Sec(0)));
    }

    [Fact]
    public void AddSnapshot_SecondSample_ComputesBytesPerSecond()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 1_000, 500)], Sec(10));
        var r = calc.AddSnapshot([S(A, 3_000, 1_500)], Sec(12))!;
        Assert.Equal(1_000d, r[A].ReceivedBytesPerSecond);
        Assert.Equal(500d, r[A].SentBytesPerSecond);
    }

    [Fact]
    public void AddSnapshot_CounterDecreased_RateIsZero()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 5_000, 5_000)], Sec(0));
        var r = calc.AddSnapshot([S(A, 100, 6_000)], Sec(1))!;
        Assert.Equal(0d, r[A].ReceivedBytesPerSecond);
        Assert.Equal(1_000d, r[A].SentBytesPerSecond);
    }

    [Fact]
    public void AddSnapshot_ZeroElapsed_ReturnsNull()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(5));
        Assert.Null(calc.AddSnapshot([S(A, 999, 999)], Sec(5)));
    }

    [Fact]
    public void AddSnapshot_LongElapsed_AveragesOverElapsed()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(0));
        var r = calc.AddSnapshot([S(A, 60_000, 0)], Sec(60))!;
        Assert.Equal(1_000d, r[A].ReceivedBytesPerSecond);
    }

    [Fact]
    public void AddSnapshot_InterfaceAppearsLater_NoRateUntilSecondSample()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(0));
        var r1 = calc.AddSnapshot([S(A, 100, 0), S(B, 9_999_999, 0)], Sec(1))!;
        Assert.False(r1.ContainsKey(B));
        var r2 = calc.AddSnapshot([S(A, 200, 0), S(B, 10_000_999, 0)], Sec(2))!;
        Assert.Equal(1_000d, r2[B].ReceivedBytesPerSecond);
    }

    [Fact]
    public void AddSnapshot_InterfaceDisappears_IsDropped()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0), S(B, 0, 0)], Sec(0));
        var r = calc.AddSnapshot([S(A, 100, 0)], Sec(1))!;
        Assert.False(r.ContainsKey(B));
        Assert.True(r.ContainsKey(A));
    }

    [Fact]
    public void ResetBaseline_NextSampleReturnsNull()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(0));
        calc.ResetBaseline();
        Assert.Null(calc.AddSnapshot([S(A, 1_000_000, 0)], Sec(1)));
    }

    [Fact]
    public void AddSnapshot_EmptyList_ReturnsEmptyAfterBaseline()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([], Sec(0));
        Assert.Empty(calc.AddSnapshot([], Sec(1))!);
    }
}
