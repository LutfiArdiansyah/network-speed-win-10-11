using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.Tests.Sampling;

public class CounterDeltaCalculatorTests
{
    private static readonly Guid A = new(1, 0, 0, new byte[8]);
    private static readonly Guid B = new(2, 0, 0, new byte[8]);

    private static InterfaceCounterSnapshot S(Guid id, ulong rx, ulong tx) => new(id, rx, tx);

    [Fact]
    public void Deltas_First_Null()
        => Assert.Null(new CounterDeltaCalculator().Add([S(A, 10, 10)]));

    [Fact]
    public void Deltas_Second_ExactBytes()
    {
        var calc = new CounterDeltaCalculator();
        calc.Add([S(A, 1_000, 500)]);
        var d = Assert.Single(calc.Add([S(A, 1_700, 900)])!);
        Assert.Equal(new ByteDelta(A, 400, 700), d);
    }

    [Fact]
    public void Deltas_CounterDecrease_IsZero()
    {
        var calc = new CounterDeltaCalculator();
        calc.Add([S(A, 5_000, 5_000)]);
        Assert.Equal(new ByteDelta(A, 1_000, 0), Assert.Single(calc.Add([S(A, 10, 6_000)])!));
    }

    [Fact]
    public void Deltas_AfterResetBaseline_NoDelta()
    {
        var calc = new CounterDeltaCalculator();
        calc.Add([S(A, 0, 0)]);
        calc.ResetBaseline();
        Assert.Null(calc.Add([S(A, 9_999_999, 9_999_999)]));
        Assert.Equal(new ByteDelta(A, 1, 1), Assert.Single(calc.Add([S(A, 10_000_000, 10_000_000)])!));
    }

    [Fact]
    public void Deltas_NewInterface_StartsNextSample()
    {
        var calc = new CounterDeltaCalculator();
        calc.Add([S(A, 0, 0)]);
        var first = calc.Add([S(A, 1, 1), S(B, 500, 500)])!;
        Assert.DoesNotContain(first, d => d.InterfaceId == B);
        var second = calc.Add([S(A, 2, 2), S(B, 600, 700)])!;
        Assert.Contains(new ByteDelta(B, 200, 100), second);
    }
}
